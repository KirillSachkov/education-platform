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

public sealed record StartLearnSessionCommand(
    Guid UserId,
    bool IsAdmin,
    Guid TopicId,
    int? QuestionCount) : ICommand;

public sealed class StartLearnSessionCommandValidator : AbstractValidator<StartLearnSessionCommand>
{
    public StartLearnSessionCommandValidator()
    {
        RuleFor(x => x.TopicId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(StartLearnSessionCommand.TopicId)));

        RuleFor(x => x.QuestionCount)
            .Must(count => count is null or (>= 1 and <= StartSessionHandler.MAX_QUESTION_COUNT))
            .WithError(GeneralErrors.OutOfRange(nameof(StartLearnSessionCommand.QuestionCount), 1, StartSessionHandler.MAX_QUESTION_COUNT));
    }
}

public sealed class StartLearnSessionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/learn-sessions",
                async Task<EndpointResult<SessionDto>> (
                    StartLearnSessionRequest request,
                    StartLearnSessionHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new StartLearnSessionCommand(user.UserId, user.IsAdmin, request.TopicId, request.QuestionCount),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Стартует LEARN-сессию («обучение по тестам», #568 Ф2) по теме: адаптивный мини-тест с
///     мгновенным фидбэком (<see cref="RevealPolicy.PER_QUESTION"/>). Резолв темы (PUBLISHED;
///     admin — DRAFT) → доступный банк (фримиум) → answer-key ECS → шафл, min(N, доступных) →
///     снапшот items (Mode=LEARN) + server-only grading-key. Возвращает SessionDto БЕЗ ключа
///     грейдинга (тот же no-leak). Каждый <c>/answers/{id}/check</c> в LEARN-сессии instant'но
///     раскрывает разбор и апсертит <c>QuestionStudyState</c> (RecordTestResult → KNOWN/WRONG +
///     SRS) — formative, без записываемого балла на пользователе. «Повтор ошибочных до усвоения» —
///     поведение клиента (повторно показывает WRONG-вопросы охвата). Нет доступных вопросов →
///     trainer.learn.no.questions.
/// </summary>
public sealed class StartLearnSessionHandler : ICommandHandler<SessionDto, StartLearnSessionCommand>
{
    public const int DEFAULT_QUESTION_COUNT = 10;

    private readonly IValidator<StartLearnSessionCommand> _validator;
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IEntitlementChecker _entitlements;
    private readonly ITransactionManager _transactions;

    public StartLearnSessionHandler(
        IValidator<StartLearnSessionCommand> validator,
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
        StartLearnSessionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == command.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(command.TopicId);

        Topic topic = topicResult.Value;
        if (!topic.IsPublished && !command.IsAdmin)
            return TrainerServiceErrors.Topic.NotPublished(command.TopicId);

        // Только STUDY-банки: MOCK-банки (собес-вопросы) в изучение/тренировку не попадают (#568).
        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(
                b => b.TopicId == command.TopicId && b.Purpose == BankPurpose.STUDY,
                cancellationToken);
        if (banks.Count == 0)
            return TrainerServiceErrors.Topic.NoBankAvailable();

        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            command.UserId, command.IsAdmin, _entitlements, cancellationToken);

        // Вопросы берутся из СОБСТВЕННОГО банка тренажёра (#623) — по всем STUDY-банкам темы
        // (bank-tier больше не гейтит доступ, dormant #674).
        var bankIds = banks.Select(b => b.Id).ToList();
        IReadOnlyList<TrainerQuestion> topicQuestions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), cancellationToken);
        if (topicQuestions.Count == 0)
            return TrainerServiceErrors.Session.LearnNoQuestions();

        // Доступ per-question (#674): не-PRO учится ТОЛЬКО на free-сэмплах; PRO/admin — на всех.
        IReadOnlyList<TrainerQuestion> accessible = hasPro
            ? topicQuestions
            : topicQuestions.Where(q => q.IsFreeSample).ToList();
        if (accessible.Count == 0)
            return TrainerServiceErrors.Topic.Locked();

        // Study-state-aware выбор (#691 t2): NEW/WRONG/REVIEW → SEEN → KNOWN, недавно виденные
        // исключаются пока есть чем добрать N; шафл внутри бакета — учим то, что ещё не усвоено, и не
        // гоняем одни и те же вопросы. Free-пул может быть меньше N → потолок min(N, доступных).
        IReadOnlyDictionary<Guid, QuestionStudyState> states = command.UserId == Guid.Empty
            ? new Dictionary<Guid, QuestionStudyState>()
            : (await _studyStates.GetForQuestionsAsync(
                    command.UserId, accessible.Select(q => q.Id).Distinct().ToList(), cancellationToken))
                .GroupBy(s => s.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());

        List<TrainerQuestion> selected = StudyAwareQuestionSelector.Select(
            accessible,
            q => q.Id,
            states,
            command.QuestionCount ?? DEFAULT_QUESTION_COUNT,
            DateTimeOffset.UtcNow).ToList();

        // LEARN — formative, мгновенный фидбэк (PER_QUESTION).
        Result<TrainingSession, Error> sessionResult =
            TrainingSession.Create(
                command.UserId,
                TrainingMode.LEARN,
                topic.TrackId,
                [command.TopicId],
                timeLimitSeconds: null,
                RevealPolicy.PER_QUESTION);
        if (sessionResult.IsFailure)
            return sessionResult.Error;

        TrainingSession session = sessionResult.Value;

        for (int i = 0; i < selected.Count; i++)
        {
            TrainerQuestion q = selected[i];

            List<SessionOptionDto> shuffledOptions = Shuffle(q.Options)
                .Select(o => new SessionOptionDto(o.Id, o.Text))
                .ToList();
            string optionsJson = JsonSerializer.Serialize(shuffledOptions, SessionMapper.JsonOptions);

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
