using System.Security.Cryptography;
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
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Sessions;
using TrainerService.Core.Features.Sessions.UseCases;
using TrainerService.Core.Features.Shared;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.MockInterviews;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.MockInterviews.UseCases;

public sealed record StartMockInterviewSessionCommand(
    Guid UserId,
    bool IsAdmin,
    Guid MockInterviewId,
    int? QuestionCount,
    int? TimeLimitSeconds) : ICommand;

public sealed class StartMockInterviewSessionCommandValidator : AbstractValidator<StartMockInterviewSessionCommand>
{
    public StartMockInterviewSessionCommandValidator()
    {
        RuleFor(x => x.MockInterviewId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartMockInterviewSessionCommand.MockInterviewId)));

        RuleFor(x => x.QuestionCount)
            .Must(count => count is null or (>= 1 and <= StartSessionHandler.MAX_QUESTION_COUNT))
            .WithError(GeneralErrors.OutOfRange(nameof(StartMockInterviewSessionCommand.QuestionCount), 1, StartSessionHandler.MAX_QUESTION_COUNT));

        RuleFor(x => x.TimeLimitSeconds)
            .Must(seconds => seconds is null or (>= 1 and <= StartMockSessionHandler.MAX_TIME_LIMIT_SECONDS))
            .WithError(GeneralErrors.OutOfRange(nameof(StartMockInterviewSessionCommand.TimeLimitSeconds), 1, StartMockSessionHandler.MAX_TIME_LIMIT_SECONDS));
    }
}

public sealed class StartMockInterviewSessionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/mock-interviews/{mockInterviewId:guid}/sessions",
                async Task<EndpointResult<SessionDto>> (
                    Guid mockInterviewId,
                    StartMockInterviewRequest request,
                    StartMockInterviewSessionHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new StartMockInterviewSessionCommand(
                            user.UserId,
                            user.IsAdmin,
                            mockInterviewId,
                            request.QuestionCount,
                            request.TimeLimitSeconds),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Стартует MOCK-сессию из именованной симуляции собеседования (#568). Грузит симуляцию
///     (PUBLISHED; admin видит DRAFT) → собирает пул вопросов: курированный (явные ссылки автора на
///     вопросы локального банка тренажёра, #623) ИЛИ legacy (по темам симуляции → их банки → вопросы).
///     Fisher–Yates шафл, для курированного — подвыборка <c>QuestionsPerSession</c> (null = весь набор),
///     legacy урезается до <see cref="MAX_LEGACY_POOL"/>. Сессия: <c>Mode=MOCK</c>, <c>trackId=null</c>,
///     <c>END_OF_SESSION</c> (отложенный разбор — мок-собес грейдит открытые ответы AI после Complete,
///     #585). Возвращает <see cref="SessionDto"/> БЕЗ ключа грейдинга. Нет вопросов → trainer.mock.no.questions.
/// </summary>
public sealed class StartMockInterviewSessionHandler : ICommandHandler<SessionDto, StartMockInterviewSessionCommand>
{
    private readonly IValidator<StartMockInterviewSessionCommand> _validator;
    private readonly IMockInterviewsRepository _mockInterviews;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IEntitlementChecker _entitlements;
    private readonly TrainerQuotaService _quota;
    private readonly ITransactionManager _transactions;

    public StartMockInterviewSessionHandler(
        IValidator<StartMockInterviewSessionCommand> validator,
        IMockInterviewsRepository mockInterviews,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        ITrainingSessionsRepository sessions,
        IEntitlementChecker entitlements,
        TrainerQuotaService quota,
        ITransactionManager transactions)
    {
        _validator = validator;
        _mockInterviews = mockInterviews;
        _banks = banks;
        _questions = questions;
        _sessions = sessions;
        _entitlements = entitlements;
        _quota = quota;
        _transactions = transactions;
    }

    public async Task<Result<SessionDto, Error>> Handle(
        StartMockInterviewSessionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Мок-собес целиком за PRO (#614): голосовой/открытый собес — подписочная фича. Admin bypass
        // внутри хелпера. Не-PRO → 403 до похода в БД.
        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            command.UserId, command.IsAdmin, _entitlements, cancellationToken);
        if (!hasPro)
            return TrainerServiceErrors.Access.ProRequired();

        // Per-user MOCK quota (#614 C2): consume one unit BEFORE pooling/LLM. Admin → ok; Pro limit
        // <= 0 → unlimited. Exceeded → 403 trainer.quota.exceeded. Fail-open on Redis error.
        UnitResult<Error> quotaResult = await _quota.TryConsumeAsync(
            command.UserId, QuotaDimension.MOCK, hasPro, cancellationToken);
        if (quotaResult.IsFailure)
            return quotaResult.Error;

        Result<MockInterview, Error> interviewResult =
            await _mockInterviews.GetByAsync(m => m.Id == command.MockInterviewId, cancellationToken);
        if (interviewResult.IsFailure)
            return TrainerServiceErrors.MockInterview.NotFound(command.MockInterviewId);

        MockInterview interview = interviewResult.Value;
        if (!interview.IsPublished && !command.IsAdmin)
            return TrainerServiceErrors.MockInterview.NotFound(command.MockInterviewId);

        // Пул вопросов: курированный (явные ссылки автора) ИЛИ legacy (по темам симуляции).
        List<PooledQuestion> pool = interview.Questions.Count > 0
            ? await BuildCuratedPoolAsync(interview, cancellationToken)
            : await BuildLegacyTopicPoolAsync(interview, cancellationToken);
        if (pool.Count == 0)
            return TrainerServiceErrors.Session.MockNoQuestions();

        // Для курированного набора берём случайную подвыборку заданного автором размера, а если размер
        // не задан — весь набор. Legacy-набор уже урезан до потолка внутри билдера, перемешиваем целиком.
        int take = interview.Questions.Count > 0
            ? interview.QuestionsPerSession ?? pool.Count
            : pool.Count;
        List<PooledQuestion> selected = Shuffle(pool).Take(take).ToList();

        // Темы сессии — уникальные темы-источники выбранных вопросов (для per-topic mastery и разбивки).
        // Если ни один вопрос не атрибутирован к теме, берём темы симуляции; если и их нет — Guid.Empty.
        List<Guid> sessionTopicIds = selected
            .Select(s => s.TopicId)
            .Where(t => t != Guid.Empty)
            .Distinct()
            .ToList();
        if (sessionTopicIds.Count == 0)
            sessionTopicIds = interview.TopicIds.Count > 0 ? interview.TopicIds.ToList() : [Guid.Empty];

        // trackId=null (симуляция кросс-трекна). END_OF_SESSION: мок-собес — отложенный разбор (#585).
        Result<TrainingSession, Error> sessionResult =
            TrainingSession.Create(
                command.UserId,
                TrainingMode.MOCK,
                trackId: null,
                sessionTopicIds,
                command.TimeLimitSeconds ?? selected.Count * 120,
                RevealPolicy.END_OF_SESSION);
        if (sessionResult.IsFailure)
            return sessionResult.Error;

        TrainingSession session = sessionResult.Value;

        for (int i = 0; i < selected.Count; i++)
        {
            PooledQuestion pooled = selected[i];
            TrainerQuestion q = pooled.Question;

            List<SessionOptionDto> shuffledOptions = Shuffle(q.Options)
                .Select(o => new SessionOptionDto(o.Id, o.Text))
                .ToList();
            string optionsJson = JsonSerializer.Serialize(shuffledOptions, SessionMapper.JsonOptions);

            GradingKey gradingKey = new(q.CorrectOptionIds, q.ReferenceAnswer, q.Explanation);
            string gradingKeyJson = JsonSerializer.Serialize(gradingKey, SessionMapper.JsonOptions);

            UnitResult<Error> addResult = session.AddItem(
                q.Id,
                pooled.TopicId,
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

    /// <summary>Потолок legacy-пула (мок-собес без курированного набора) — sane default, чтобы не выдать сотни вопросов.</summary>
    private const int MAX_LEGACY_POOL = 20;

    /// <summary>
    ///     Строит пул из курированного набора автора: тянет вопросы локального банка по их id (#623).
    ///     Висячая ссылка (удалённый вопрос) пропускается. TopicId вопроса резолвится через его банк
    ///     (для per-topic mastery). Порядок не важен — пул всё равно шафлится.
    /// </summary>
    private async Task<List<PooledQuestion>> BuildCuratedPoolAsync(
        MockInterview interview,
        CancellationToken cancellationToken)
    {
        var curatedQuestionIds = interview.Questions.Select(q => q.QuestionId).ToHashSet();
        IReadOnlyList<TrainerQuestion> questions =
            await _questions.GetManyByAsync(q => curatedQuestionIds.Contains(q.Id), cancellationToken);
        Dictionary<Guid, TrainerQuestion> questionById = questions.ToDictionary(q => q.Id);

        // BankId → TopicId (для атрибуции mastery).
        var bankIds = questions.Select(q => q.BankId).ToHashSet();
        IReadOnlyList<TopicBank> banks = bankIds.Count == 0
            ? []
            : await _banks.GetManyByAsync(b => bankIds.Contains(b.Id), cancellationToken);
        Dictionary<Guid, Guid> topicByBank = banks.ToDictionary(b => b.Id, b => b.TopicId);

        var pool = new List<PooledQuestion>();
        foreach (MockInterviewQuestion reference in interview.Questions.OrderBy(q => q.SortIndex))
        {
            if (!questionById.TryGetValue(reference.QuestionId, out TrainerQuestion? question))
                continue; // удалённый вопрос — пропускаем висячую ссылку, не валим сессию.

            Guid topicId = topicByBank.GetValueOrDefault(question.BankId, Guid.Empty);
            pool.Add(new PooledQuestion(topicId, question));
        }

        return pool;
    }

    /// <summary>
    ///     Legacy-пул (мок-собес без курированного набора): темы симуляции → их банки любого назначения
    ///     → вопросы локального банка → урезанные до <see cref="MAX_LEGACY_POOL"/> (sane default, #623).
    /// </summary>
    private async Task<List<PooledQuestion>> BuildLegacyTopicPoolAsync(
        MockInterview interview,
        CancellationToken cancellationToken)
    {
        var topicIds = interview.TopicIds.ToHashSet();
        if (topicIds.Count == 0)
            return [];

        // Банки тем симуляции — ЛЮБОГО назначения (STUDY и MOCK оба питают mock-собес). Весь мок-собес
        // уже за PRO-гейтом (см. Handle), поэтому PAID-банки тоже в пуле.
        IReadOnlyList<TopicBank> allBanks =
            await _banks.GetManyByAsync(b => topicIds.Contains(b.TopicId), cancellationToken);
        if (allBanks.Count == 0)
            return [];

        var bankIds = allBanks.Select(b => b.Id).ToList();
        Dictionary<Guid, Guid> topicByBank = allBanks.ToDictionary(b => b.Id, b => b.TopicId);

        IReadOnlyList<TrainerQuestion> questions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), cancellationToken);

        var pool = questions
            .Select(q => new PooledQuestion(topicByBank[q.BankId], q))
            .ToList();

        // Урезаем до sane-потолка (а не весь набор) — Shuffle для равномерной выборки до cap'а.
        return pool.Count <= MAX_LEGACY_POOL ? pool : Shuffle(pool).Take(MAX_LEGACY_POOL).ToList();
    }

    /// <summary>Fisher–Yates shuffle через криптослучайность (без bias).</summary>
    private static List<T> Shuffle<T>(IReadOnlyList<T> source)
    {
        var list = source.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>Вопрос в пуле + его тема-источник для атрибуции при снапшоте.</summary>
    private sealed record PooledQuestion(Guid TopicId, TrainerQuestion Question);
}
