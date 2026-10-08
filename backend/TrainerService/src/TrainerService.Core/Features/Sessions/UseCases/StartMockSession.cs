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
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Sessions.Selection;
using TrainerService.Core.Features.Shared;
using TrainerService.Core.Features.Topics;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.UseCases;

public sealed record StartMockSessionCommand(
    Guid UserId,
    bool IsAdmin,
    Guid TrackId,
    int? QuestionCount,
    int? TimeLimitSeconds,
    string? Difficulty,
    string? Direction) : ICommand;

public sealed class StartMockSessionCommandValidator : AbstractValidator<StartMockSessionCommand>
{
    public StartMockSessionCommandValidator()
    {
        RuleFor(x => x.TrackId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartMockSessionCommand.TrackId)));

        RuleFor(x => x.QuestionCount)
            .Must(count => count is null or (>= 1 and <= StartSessionHandler.MAX_QUESTION_COUNT))
            .WithError(GeneralErrors.OutOfRange(nameof(StartMockSessionCommand.QuestionCount), 1, StartSessionHandler.MAX_QUESTION_COUNT));

        RuleFor(x => x.TimeLimitSeconds)
            .Must(seconds => seconds is null or (>= 1 and <= StartMockSessionHandler.MAX_TIME_LIMIT_SECONDS))
            .WithError(GeneralErrors.OutOfRange(nameof(StartMockSessionCommand.TimeLimitSeconds), 1, StartMockSessionHandler.MAX_TIME_LIMIT_SECONDS));
    }
}

public sealed class StartMockSessionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/mock-sessions",
                async Task<EndpointResult<SessionDto>> (
                    StartMockSessionRequest request,
                    StartMockSessionHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new StartMockSessionCommand(
                            user.UserId,
                            user.IsAdmin,
                            request.TrackId,
                            request.QuestionCount,
                            request.TimeLimitSeconds,
                            request.Difficulty,
                            request.Direction),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Стартует MOCK-сессию (симуляция собеса) по треку. Собирает PUBLISHED-темы трека
///     (опц. сужено по <c>Direction</c> — #568 Ф2) → их доступные банки (фримиум: FREE — любому,
///     PAID — admin) → тянет answer-key каждого банка из ECS → пулит вопросы кросс-тематически
///     (каждый помнит свою тему), опц. фильтрует по сложности, ШАФЛИТ, берёт min(N, доступных) →
///     снапшотит items (Mode=MOCK, TimeLimitSeconds) + server-only grading-key. Возвращает
///     SessionDto БЕЗ ключа грейдинга (тот же no-leak, что и DRILL). Нет доступных вопросов →
///     trainer.mock.no.questions. MOCK сохраняет instant-feedback (PER_QUESTION).
/// </summary>
public sealed class StartMockSessionHandler : ICommandHandler<SessionDto, StartMockSessionCommand>
{
    public const int DEFAULT_QUESTION_COUNT = 15;
    public const int MAX_TIME_LIMIT_SECONDS = 4 * 60 * 60; // 4 часа — потолок информативного таймера.

    private readonly IValidator<StartMockSessionCommand> _validator;
    private readonly ITracksRepository _tracks;
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IEntitlementChecker _entitlements;
    private readonly TrainerQuotaService _quota;
    private readonly ITransactionManager _transactions;

    public StartMockSessionHandler(
        IValidator<StartMockSessionCommand> validator,
        ITracksRepository tracks,
        ITopicsRepository topics,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        IQuestionStudyStatesRepository studyStates,
        ITrainingSessionsRepository sessions,
        IEntitlementChecker entitlements,
        TrainerQuotaService quota,
        ITransactionManager transactions)
    {
        _validator = validator;
        _tracks = tracks;
        _topics = topics;
        _banks = banks;
        _questions = questions;
        _studyStates = studyStates;
        _sessions = sessions;
        _entitlements = entitlements;
        _quota = quota;
        _transactions = transactions;
    }

    public async Task<Result<SessionDto, Error>> Handle(
        StartMockSessionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Симуляция собеса целиком за PRO (#614): бесплатный tier = текстовые ответы в FREE-банках.
        // Admin bypass внутри хелпера. Не-PRO → 403 до похода в БД/ECS.
        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            command.UserId, command.IsAdmin, _entitlements, cancellationToken);
        if (!hasPro)
            return TrainerServiceErrors.Access.ProRequired();

        // Per-user MOCK quota (#614 C2): consume one unit BEFORE pooling/ECS/LLM. Admin → ok; Pro limit
        // <= 0 → unlimited. Exceeded → 403 trainer.quota.exceeded. Fail-open on Redis error.
        UnitResult<Error> quotaResult = await _quota.TryConsumeAsync(
            command.UserId, QuotaDimension.MOCK, hasPro, cancellationToken);
        if (quotaResult.IsFailure)
            return quotaResult.Error;

        bool trackExists = await _tracks.ExistsAsync(t => t.Id == command.TrackId, cancellationToken);
        if (!trackExists)
            return TrainerServiceErrors.Track.NotFound(command.TrackId);

        QuestionDifficulty? difficultyFilter = null;
        if (!string.IsNullOrWhiteSpace(command.Difficulty))
        {
            if (!Enum.TryParse(command.Difficulty.Trim(), ignoreCase: false, out QuestionDifficulty parsed)
                || !Enum.IsDefined(parsed))
            {
                return TrainerServiceErrors.Bank.InvalidDifficulty(command.Difficulty);
            }

            difficultyFilter = parsed;
        }

        // Опциональный scope по направлению трека (#568 Ф2) — сужает пул тем до этого направления.
        Result<TopicDirection?, Error> directionResult = TopicDirectionParser.Parse(command.Direction);
        if (directionResult.IsFailure)
            return directionResult.Error;

        TopicDirection? directionFilter = directionResult.Value;

        // PUBLISHED-темы трека (admin видит и DRAFT — параллельно с тем, как StartSession пускает
        // admin на неопубликованную тему); опционально сужены по направлению.
        IReadOnlyList<Topic> topics = await _topics.GetManyByAsync(
            t => t.TrackId == command.TrackId
                && (t.IsPublished || command.IsAdmin)
                && (directionFilter == null || t.Direction == directionFilter),
            cancellationToken);
        if (topics.Count == 0)
            return TrainerServiceErrors.Session.MockNoQuestions();

        var topicIds = topics.Select(t => t.Id).ToHashSet();

        IReadOnlyList<TopicBank> allBanks =
            await _banks.GetManyByAsync(b => topicIds.Contains(b.TopicId), cancellationToken);
        if (allBanks.Count == 0)
            return TrainerServiceErrors.Session.MockNoQuestions();

        // Мок целиком за PRO-гейтом (см. Handle): PRO/admin видит ВСЕ банки трека. Bank-tier больше не
        // гейтит доступ (dormant #674). Вопросы — из собственного банка тренажёра (#623), один fetch по
        // набору банков, каждый вопрос атрибутируем теме-владельцу его банка (mastery → правильная тема).
        var bankIds = allBanks.Select(b => b.Id).ToList();
        Dictionary<Guid, Guid> topicByBank = allBanks.ToDictionary(b => b.Id, b => b.TopicId);

        IReadOnlyList<TrainerQuestion> bankQuestions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), cancellationToken);

        var pool = new List<PooledQuestion>();
        foreach (TrainerQuestion question in bankQuestions)
        {
            if (difficultyFilter is { } df
                && !string.Equals(question.Difficulty?.ToString(), df.ToString(), StringComparison.Ordinal))
            {
                continue;
            }

            pool.Add(new PooledQuestion(topicByBank[question.BankId], question));
        }

        if (pool.Count == 0)
            return TrainerServiceErrors.Session.MockNoQuestions();

        // Study-state-aware выбор (#691 t2): кросс-тематический пул приоритизируем по study-state —
        // NEW/WRONG/REVIEW → SEEN → KNOWN, недавно виденные исключаются пока есть чем добрать N; шафл
        // внутри бакета. Так мок-набор не повторяет одни и те же вопросы из попытки в попытку.
        IReadOnlyDictionary<Guid, QuestionStudyState> states =
            (await _studyStates.GetForQuestionsAsync(
                command.UserId, pool.Select(p => p.Question.Id).Distinct().ToList(), cancellationToken))
            .GroupBy(s => s.QuestionId)
            .ToDictionary(g => g.Key, g => g.First());

        List<PooledQuestion> selected = StudyAwareQuestionSelector.Select(
            pool,
            p => p.Question.Id,
            states,
            command.QuestionCount ?? DEFAULT_QUESTION_COUNT,
            DateTimeOffset.UtcNow).ToList();

        // Темы, реально попавшие в сессию (для TopicIds сессии — для истории/прогресса).
        List<Guid> sessionTopicIds = selected.Select(p => p.TopicId).Distinct().ToList();

        // MOCK сохраняет исторический instant-feedback (PER_QUESTION) — разбор сразу после каждого
        // ответа, как до Ф2. Grade-at-end — отдельная опция тест-сессии (StartSession.RevealPolicy).
        Result<TrainingSession, Error> sessionResult =
            TrainingSession.Create(
                command.UserId,
                TrainingMode.MOCK,
                command.TrackId,
                sessionTopicIds,
                command.TimeLimitSeconds,
                RevealPolicy.PER_QUESTION);
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
