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
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.UseCases;

public sealed record StartSessionCommand(
    Guid UserId,
    bool IsAdmin,
    string? Mode,
    Guid TopicId,
    int? QuestionCount,
    string? RevealPolicy,
    IReadOnlyList<Guid>? QuestionIds) : ICommand;

public sealed class StartSessionCommandValidator : AbstractValidator<StartSessionCommand>
{
    public StartSessionCommandValidator()
    {
        RuleFor(x => x.TopicId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartSessionCommand.TopicId)));

        RuleFor(x => x.QuestionCount)
            .Must(count => count is null or (>= 1 and <= StartSessionHandler.MAX_QUESTION_COUNT))
            .WithError(GeneralErrors.OutOfRange(nameof(StartSessionCommand.QuestionCount), 1, StartSessionHandler.MAX_QUESTION_COUNT));

        RuleFor(x => x.QuestionIds)
            .Must(ids => ids is null || (ids.Count >= 1 && ids.Count <= StartSessionHandler.MAX_QUESTION_COUNT))
            .WithError(GeneralErrors.OutOfRange(nameof(StartSessionCommand.QuestionIds), 1, StartSessionHandler.MAX_QUESTION_COUNT));
    }
}

public sealed class StartSessionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/sessions",
                async Task<EndpointResult<SessionDto>> (
                    StartSessionRequest request,
                    StartSessionHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new StartSessionCommand(
                            user.UserId,
                            user.IsAdmin,
                            request.Mode,
                            request.TopicId,
                            request.QuestionCount,
                            request.RevealPolicy,
                            request.QuestionIds),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Стартует DRILL/тест-сессию по теме. Резолвит тему (PUBLISHED) → выбирает доступный банк
///     (фримиум) → берёт вопросы собственного банка → ШАФЛИТ, берёт min(N, доступных)
///     → на каждый создаёт снапшот item'а (вопрос/варианты-ШАФЛ/секция/сложность) + server-only
///     снапшот ключа грейдинга → персистит сессию (IN_PROGRESS) → возвращает SessionDto БЕЗ
///     ключа грейдинга / правильных ответов.
///     <para>
///         <b>RevealPolicy (#568 Ф2):</b> опционально принимается на старте. Пусто → <c>PER_QUESTION</c>
///         (исторический DRILL с мгновенной проверкой). <c>END_OF_SESSION</c> → grade-at-end тест:
///         каждый ответ грейдится «вслепую», разбор и счёт раскрываются после <c>Complete</c>.
///     </para>
/// </summary>
public sealed class StartSessionHandler : ICommandHandler<SessionDto, StartSessionCommand>
{
    public const int MAX_QUESTION_COUNT = 50;
    public const int DEFAULT_QUESTION_COUNT = 10;

    private const string DRILL_MODE = "DRILL";

    private readonly IValidator<StartSessionCommand> _validator;
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IEntitlementChecker _entitlements;
    private readonly ITransactionManager _transactions;

    public StartSessionHandler(
        IValidator<StartSessionCommand> validator,
        ITopicsRepository topics,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        IQuestionStudyStatesRepository studyStates,
        ITrainingSessionsRepository sessions,
        IEntitlementChecker entitlements,
        ITransactionManager transactions)
    {
        _validator = validator;
        _topics = topics;
        _banks = banks;
        _questions = questions;
        _studyStates = studyStates;
        _sessions = sessions;
        _entitlements = entitlements;
        _transactions = transactions;
    }

    public async Task<Result<SessionDto, Error>> Handle(
        StartSessionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Пустой mode трактуем как DRILL. LEARN и MOCK стартуют через свои отдельные эндпоинты.
        if (!string.IsNullOrWhiteSpace(command.Mode)
            && !string.Equals(command.Mode, DRILL_MODE, StringComparison.Ordinal))
        {
            return TrainerServiceErrors.Session.InvalidMode(command.Mode);
        }

        // RevealPolicy: пусто → PER_QUESTION (исторический instant DRILL). END_OF_SESSION = grade-at-end.
        Result<RevealPolicy, Error> revealResult = ParseRevealPolicy(command.RevealPolicy);
        if (revealResult.IsFailure)
            return revealResult.Error;

        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == command.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(command.TopicId);

        Topic topic = topicResult.Value;
        if (!topic.IsPublished && !command.IsAdmin)
            return TrainerServiceErrors.Topic.NotPublished(command.TopicId);

        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(b => b.TopicId == command.TopicId, cancellationToken);
        if (banks.Count == 0)
            return TrainerServiceErrors.Topic.NoBankAvailable();

        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            command.UserId, command.IsAdmin, _entitlements, cancellationToken);

        // Вопросы берутся из СОБСТВЕННОГО банка тренажёра (#623) — по всем банкам темы (bank-tier
        // больше не гейтит доступ, dormant #674).
        var bankIds = banks.Select(b => b.Id).ToList();
        IReadOnlyList<TrainerQuestion> topicQuestions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), cancellationToken);
        if (topicQuestions.Count == 0)
            return TrainerServiceErrors.Session.NoQuestions();

        // Доступ per-question (#674): не-PRO рисует снапшот ТОЛЬКО из free-сэмплов (заблокированный
        // контент не попадает в активную сессию); PRO/admin — из всех. Нет доступных → тема заперта.
        IReadOnlyList<TrainerQuestion> accessible = hasPro
            ? topicQuestions
            : topicQuestions.Where(q => q.IsFreeSample).ToList();
        if (accessible.Count == 0)
            return TrainerServiceErrors.Topic.Locked();

        List<TrainerQuestion> selected;
        if (command.QuestionIds is { Count: > 0 })
        {
            // Точный набор вопросов (мини-тест/«юнит» темы): берём ровно ДОСТУПНЫЕ из них в заданном
            // порядке, без шафла. Несуществующие/недоступные id отбрасываем; пусто → нет вопросов.
            Dictionary<Guid, TrainerQuestion> byId = accessible.ToDictionary(q => q.Id);
            selected = command.QuestionIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
            if (selected.Count == 0)
                return TrainerServiceErrors.Session.NoQuestions();
        }
        else
        {
            // Study-state-aware выбор (#691 t2): NEW/WRONG/REVIEW → SEEN → KNOWN, недавно виденные
            // исключаются пока есть чем добрать N; шафл внутри бакета. Так вопросы не повторяются от
            // сессии к сессии. Free-пул может быть меньше N → финальный потолок min(N, доступных).
            IReadOnlyDictionary<Guid, QuestionStudyState> states =
                await LoadStudyStatesAsync(command.UserId, accessible.Select(q => q.Id), cancellationToken);

            selected = StudyAwareQuestionSelector.Select(
                accessible,
                q => q.Id,
                states,
                command.QuestionCount ?? DEFAULT_QUESTION_COUNT,
                DateTimeOffset.UtcNow).ToList();
        }

        Result<TrainingSession, Error> sessionResult =
            TrainingSession.Create(
                command.UserId,
                TrainingMode.DRILL,
                topic.TrackId,
                [command.TopicId],
                timeLimitSeconds: null,
                revealResult.Value);
        if (sessionResult.IsFailure)
            return sessionResult.Error;

        TrainingSession session = sessionResult.Value;

        for (int i = 0; i < selected.Count; i++)
        {
            TrainerQuestion q = selected[i];

            // Снапшот вариантов — ШАФЛ, без признака правильности.
            List<SessionOptionDto> shuffledOptions = Shuffle(q.Options)
                .Select(o => new SessionOptionDto(o.Id, o.Text))
                .ToList();
            string optionsJson = JsonSerializer.Serialize(shuffledOptions, SessionMapper.JsonOptions);

            // Server-only снапшот ключа грейдинга.
            GradingKey gradingKey = new(q.CorrectOptionIds, q.ReferenceAnswer, q.Explanation);
            string gradingKeyJson = JsonSerializer.Serialize(gradingKey, SessionMapper.JsonOptions);

            UnitResult<Error> addResult = session.AddItem(
                q.Id,
                command.TopicId,
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

    /// <summary>
    ///     Парсит RevealPolicy из запроса. Пусто → PER_QUESTION (исторический instant DRILL).
    ///     Невалидное значение → доменная ошибка (не молча подменяем default'ом).
    /// </summary>
    internal static Result<RevealPolicy, Error> ParseRevealPolicy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return RevealPolicy.PER_QUESTION;

        if (Enum.TryParse(raw.Trim(), ignoreCase: false, out RevealPolicy parsed) && Enum.IsDefined(parsed))
            return parsed;

        return TrainerServiceErrors.Session.InvalidRevealPolicy(raw);
    }

    /// <summary>
    ///     Грузит study-state'ы вызывающего по набору вопросов-кандидатов (#691 t2) — для
    ///     study-aware выбора. Анонимов тут нет (endpoint требует Content.VIEW), но если userId
    ///     пустой — пустой словарь (все вопросы трактуются как NEW). Переиспользует существующий
    ///     repo-метод <c>GetForQuestionsAsync</c> (тот же, что питает список вопросов охвата).
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, QuestionStudyState>> LoadStudyStatesAsync(
        Guid userId,
        IEnumerable<Guid> questionIds,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return new Dictionary<Guid, QuestionStudyState>();

        return (await _studyStates.GetForQuestionsAsync(userId, questionIds.Distinct().ToList(), cancellationToken))
            .GroupBy(s => s.QuestionId)
            .ToDictionary(g => g.Key, g => g.First());
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
}
