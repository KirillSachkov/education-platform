using System.Text.Json;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.UseCases;

public sealed record StartReviewSessionCommand(
    Guid UserId,
    bool IsAdmin,
    IReadOnlyList<Guid> QuestionIds) : ICommand;

public sealed class StartReviewSessionCommandValidator : AbstractValidator<StartReviewSessionCommand>
{
    public StartReviewSessionCommandValidator()
    {
        RuleFor(x => x.QuestionIds)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartReviewSessionCommand.QuestionIds)));
    }
}

public sealed class StartReviewSessionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/review-sessions",
                async Task<EndpointResult<SessionDto>> (
                    StartReviewSessionRequest request,
                    StartReviewSessionHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new StartReviewSessionCommand(user.UserId, user.IsAdmin, request.QuestionIds ?? []),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Стартует REVIEW-сессию (#568 Ф2) — тест по ПРОИЗВОЛЬНОМУ набору вопросов («Доучить» по
///     ошибкам / «Пройти тест по закладке»). Зеркалит <see cref="StartLearnSessionHandler"/>
///     (Mode=LEARN, RevealPolicy=PER_QUESTION, тот же снапшот item'ов + server-only grading-key,
///     тот же no-leak DTO), но вместо резолва вопросов из банка одной темы принимает набор
///     <c>questionIds</c> и собирает сессию ровно из них.
///     <para>
///         Резолв: собираем все доступные пользователю STUDY-банки (фримиум-гейт — FREE всем,
///         PAID admin'у), строим индекс <c>questionId → (topicId, quizId, вопрос)</c> по их
///         answer-key'ам (дедуп S2S-fetch по quizId) и выбираем запрошенные вопросы В ЗАДАННОМ
///         ПОРЯДКЕ. Недоступные/несуществующие id молча отбрасываются (как точный набор в
///         <see cref="StartSessionHandler"/>); набор клампится до
///         <see cref="StartSessionHandler.MAX_QUESTION_COUNT"/>. Сессия кросс-тематическая
///         (trackId=null), каждый item помнит свою тему-источник (mastery → правильная тема).
///         Пусто/всё недоступно → <c>trainer.review.no.questions</c>. Драйвится тем же
///         <c>check</c>/<c>complete</c>-флоу, что и LEARN.
///     </para>
/// </summary>
public sealed class StartReviewSessionHandler : ICommandHandler<SessionDto, StartReviewSessionCommand>
{
    private readonly IValidator<StartReviewSessionCommand> _validator;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IEntitlementChecker _entitlements;
    private readonly ITransactionManager _transactions;

    public StartReviewSessionHandler(
        IValidator<StartReviewSessionCommand> validator,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        ITrainingSessionsRepository sessions,
        IEntitlementChecker entitlements,
        ITransactionManager transactions)
    {
        _validator = validator;
        _banks = banks;
        _questions = questions;
        _sessions = sessions;
        _entitlements = entitlements;
        _transactions = transactions;
    }

    public async Task<Result<SessionDto, Error>> Handle(
        StartReviewSessionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Запрошенные id, дедуп с сохранением порядка, клампим до потолка теста.
        var requestedOrder = new List<Guid>();
        var requestedSet = new HashSet<Guid>();
        foreach (Guid id in command.QuestionIds)
        {
            if (id != Guid.Empty && requestedSet.Add(id))
                requestedOrder.Add(id);
        }

        if (requestedOrder.Count > StartSessionHandler.MAX_QUESTION_COUNT)
            requestedOrder = requestedOrder.Take(StartSessionHandler.MAX_QUESTION_COUNT).ToList();

        if (requestedOrder.Count == 0)
            return TrainerServiceErrors.Session.ReviewNoQuestions();

        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            command.UserId, command.IsAdmin, _entitlements, cancellationToken);

        // STUDY-банки (учебные) — питают «доучить»/«тест по закладке». MOCK-only банки сюда не попадают
        // (зеркало GetQuestionList). Bank-tier больше не гейтит доступ (dormant #674).
        IReadOnlyList<TopicBank> studyBanks =
            await _banks.GetManyByAsync(b => b.Purpose == BankPurpose.STUDY, cancellationToken);

        var bankIds = studyBanks.Select(b => b.Id).ToList();
        Dictionary<Guid, Guid> topicByBank = studyBanks.ToDictionary(b => b.Id, b => b.TopicId);

        // Индекс questionId → источник: фетчим ТОЛЬКО запрошенные вопросы (capped MAX_QUESTION_COUNT),
        // а не весь банк платформы — иначе O(всех вопросов) на каждый старт ревью (#674, code-review SF-3).
        IReadOnlyList<TrainerQuestion> bankQuestions = bankIds.Count == 0
            ? []
            : await _questions.GetManyByAsync(
                q => requestedOrder.Contains(q.Id) && bankIds.Contains(q.BankId), cancellationToken);

        // Доступ per-question (#674): не-PRO повторяет ТОЛЬКО free-сэмплы; PRO/admin — любые. Недоступный/
        // несуществующий запрошенный id молча отбрасывается (как точный набор в StartSession).
        var resolved = new Dictionary<Guid, ResolvedReviewQuestion>();
        foreach (TrainerQuestion question in bankQuestions)
        {
            if (!hasPro && !question.IsFreeSample)
                continue;

            // Вопрос принадлежит ровно одному банку → одна тема-источник.
            resolved.TryAdd(question.Id, new ResolvedReviewQuestion(topicByBank[question.BankId], question));
        }

        // Выбираем запрошенные вопросы в заданном порядке; отсутствующие/недоступные отбрасываем.
        List<ResolvedReviewQuestion> selected = requestedOrder
            .Where(resolved.ContainsKey)
            .Select(id => resolved[id])
            .ToList();
        if (selected.Count == 0)
            return TrainerServiceErrors.Session.ReviewNoQuestions();

        List<Guid> sessionTopicIds = selected.Select(s => s.TopicId).Distinct().ToList();

        // REVIEW = LEARN-сессия: formative, PER_QUESTION (мгновенный фидбэк). Кросс-тематическая,
        // поэтому trackId=null.
        Result<TrainingSession, Error> sessionResult =
            TrainingSession.Create(
                command.UserId,
                TrainingMode.LEARN,
                trackId: null,
                sessionTopicIds,
                timeLimitSeconds: null,
                RevealPolicy.PER_QUESTION);
        if (sessionResult.IsFailure)
            return sessionResult.Error;

        TrainingSession session = sessionResult.Value;

        for (int i = 0; i < selected.Count; i++)
        {
            ResolvedReviewQuestion source = selected[i];
            TrainerQuestion q = source.Question;

            List<SessionOptionDto> shuffledOptions = Shuffle(q.Options)
                .Select(o => new SessionOptionDto(o.Id, o.Text))
                .ToList();
            string optionsJson = JsonSerializer.Serialize(shuffledOptions, SessionMapper.JsonOptions);

            GradingKey gradingKey = new(q.CorrectOptionIds, q.ReferenceAnswer, q.Explanation);
            string gradingKeyJson = JsonSerializer.Serialize(gradingKey, SessionMapper.JsonOptions);

            UnitResult<Error> addResult = session.AddItem(
                q.Id,
                source.TopicId,
                q.Type.ToString(),
                q.Stem,
                optionsJson,
                q.Section,
                q.Difficulty?.ToString(),
                i,
                gradingKeyJson);
            if (addResult.IsFailure)
                return addResult.Error;
        }

        await _sessions.AddAsync(session, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return SessionMapper.ToDto(session, hasPro);
    }

    /// <summary>Fisher–Yates shuffle через криптослучайность (без bias) — для порядка вариантов.</summary>
    private static List<T> Shuffle<T>(IReadOnlyList<T> source)
    {
        var list = source.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>Резолвнутый вопрос + его тема-источник для снапшота item'а.</summary>
    private sealed record ResolvedReviewQuestion(Guid TopicId, TrainerQuestion Question);
}
