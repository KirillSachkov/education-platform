using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.Questions.UseCases;

/// <summary>Сводка backfill'а эталонов: сколько сгенерировано, пропущено (уже заполнены) и не удалось (AI-сбой).</summary>
public sealed record BackfillReferenceAnswersResponse(int Generated, int Skipped, int Failed);

/// <summary>
///     Admin-backfill (#691 t6): для OPEN_TEXT-вопросов с пустым эталоном генерирует черновой эталон через
///     AI. Опц. сужается <see cref="BankId"/> (точечно) или <see cref="TopicId"/> (все банки темы);
///     <see cref="BankId"/> приоритетнее. Без scope — по всем банкам. <see cref="Limit"/> ограничивает
///     число missing-reference вопросов, обрабатываемых за один вызов (cap AI-стоимости/DoS).
/// </summary>
public sealed record BackfillReferenceAnswersCommand(Guid? TopicId, Guid? BankId, int? Limit) : ICommand;

public sealed class BackfillReferenceAnswersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/admin/questions/backfill-reference-answers",
                async Task<EndpointResult<BackfillReferenceAnswersResponse>> (
                    BackfillReferenceAnswersHandler handler,
                    CancellationToken cancellationToken,
                    Guid? topicId = null,
                    Guid? bankId = null,
                    int? limit = null) =>
                    await handler.Handle(
                        new BackfillReferenceAnswersCommand(topicId, bankId, limit), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN)
            .RequireRateLimiting("admin-stats");
    }
}

/// <summary>
///     Находит OPEN_TEXT-вопросы (в выбранном scope) с пустым <c>ReferenceAnswer</c>, генерирует
///     черновой эталон через <see cref="IReferenceAnswerGenerator"/> и сохраняет его на агрегат
///     <see cref="TrainerQuestion"/> (через <c>Update</c>) одной транзакцией. Идемпотентен: уже
///     заполненные эталоны пропускаются (повторный прогон → 0 сгенерировано). Choice/EXACT_TEXT
///     вопросы в выборку не попадают (фильтр по типу). AI-сбой на отдельном вопросе не валит весь
///     запрос — он считается в <c>Failed</c>, остальные сохраняются. За один вызов обрабатывается не
///     более <c>limit</c> (clamp 1..500, default 50) missing-reference вопросов — bounds AI-стоимости;
///     повторные вызовы доедают backlog (уже заполненные пропускаются).
/// </summary>
public sealed class BackfillReferenceAnswersHandler
    : ICommandHandler<BackfillReferenceAnswersResponse, BackfillReferenceAnswersCommand>
{
    private const int DEFAULT_LIMIT = 50;
    private const int MAX_LIMIT = 500;

    // OPEN_TEXT carries no options — Update() requires the param but applies none for text types.
    private static readonly IReadOnlyList<(string Text, bool IsCorrect)> NoOptions = [];

    private readonly ITrainerQuestionsRepository _questions;
    private readonly ITopicBanksRepository _banks;
    private readonly IReferenceAnswerGenerator _generator;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<BackfillReferenceAnswersHandler> _logger;

    public BackfillReferenceAnswersHandler(
        ITrainerQuestionsRepository questions,
        ITopicBanksRepository banks,
        IReferenceAnswerGenerator generator,
        ITransactionManager transactions,
        ILogger<BackfillReferenceAnswersHandler> logger)
    {
        _questions = questions;
        _banks = banks;
        _generator = generator;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<Result<BackfillReferenceAnswersResponse, Error>> Handle(
        BackfillReferenceAnswersCommand command,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid>? scopedBankIds = await ResolveScopeAsync(command, cancellationToken);

        // Scope resolved but matched no banks (e.g. unknown topic / bank id) → nothing to do, no LLM call.
        if (scopedBankIds is { Count: 0 })
            return new BackfillReferenceAnswersResponse(0, 0, 0);

        // Server-side cap on AI calls per invocation (cost / DoS). Repeated calls drain the backlog
        // because already-filled questions are skipped (don't count against the cap).
        int cap = Math.Clamp(command.Limit ?? DEFAULT_LIMIT, 1, MAX_LIMIT);

        // Count already-filled rows in SQL for the response, then load only the bounded missing backlog.
        // The old path materialised every OPEN_TEXT question (and its options navigation) before applying
        // the cap, so a maintenance call grew linearly with the entire question bank.
        int skipped = scopedBankIds is null
            ? await _questions.CountByAsync(
                q => q.Type == TrainerQuestionType.OPEN_TEXT && q.ReferenceAnswer != null,
                cancellationToken)
            : await _questions.CountByAsync(
                q => q.Type == TrainerQuestionType.OPEN_TEXT
                    && q.ReferenceAnswer != null
                    && scopedBankIds.Contains(q.BankId),
                cancellationToken);
        IReadOnlyList<TrainerQuestion> openQuestions = await _questions.GetOpenWithoutReferenceAsync(
            scopedBankIds,
            cap,
            cancellationToken);

        int generated = 0;
        int failed = 0;

        foreach (TrainerQuestion question in openQuestions)
        {
            Result<string, Error> draft = await _generator.GenerateAsync(
                question.Stem, question.Explanation, question.Section, cancellationToken);
            if (draft.IsFailure)
            {
                _logger.LogWarning(
                    "Reference-answer generation failed for question {QuestionId}: {Code}",
                    question.Id, draft.Error.Messages[0].Code);
                failed++;
                continue;
            }

            UnitResult<Error> update = question.Update(
                question.Stem,
                question.Type,
                draft.Value,
                question.Explanation,
                question.Difficulty,
                question.Section,
                NoOptions);
            if (update.IsFailure)
            {
                failed++;
                continue;
            }

            generated++;
        }

        if (generated > 0)
        {
            UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
            if (save.IsFailure)
                return save.Error;
        }

        return new BackfillReferenceAnswersResponse(generated, skipped, failed);
    }

    /// <summary>
    ///     Резолвит scope в список bank-id'ов: <c>null</c> = без ограничения (все банки); список
    ///     (возможно пустой) = только эти банки. <see cref="BackfillReferenceAnswersCommand.BankId"/>
    ///     приоритетнее <see cref="BackfillReferenceAnswersCommand.TopicId"/>.
    /// </summary>
    private async Task<IReadOnlyList<Guid>?> ResolveScopeAsync(
        BackfillReferenceAnswersCommand command, CancellationToken ct)
    {
        if (command.BankId is Guid bankId)
            return [bankId];

        if (command.TopicId is Guid topicId)
        {
            IReadOnlyList<TopicBank> banks = await _banks.GetManyByAsync(b => b.TopicId == topicId, ct);
            return banks.Select(b => b.Id).ToList();
        }

        return null;
    }
}
