using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Quizzes;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Database;
using ProgressService.Core.Diagnostics;
using ProgressService.Core.Extensions;
using ProgressService.Core.Features.LevelTests.IntegrationEvents;
using ProgressService.Domain;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Core.Features.LevelTests.UseCases;

public sealed record SubmitLevelTestAttemptCommand(
    Guid QuizId,
    string? AnonymousId,
    IReadOnlyList<SubmitLevelTestAnswerItem> Answers) : ICommand;

public sealed class SubmitLevelTestAttemptCommandValidator : AbstractValidator<SubmitLevelTestAttemptCommand>
{
    public const int MAX_ANSWERS = 100;

    public SubmitLevelTestAttemptCommandValidator()
    {
        RuleFor(x => x.QuizId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SubmitLevelTestAttemptCommand.QuizId)));

        RuleFor(x => x.Answers)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SubmitLevelTestAttemptCommand.Answers)));

        RuleFor(x => x.Answers)
            .Must(answers => answers is null || answers.Count <= MAX_ANSWERS)
            .WithError(ProgressErrors.QuizAttemptTooManyAnswers(MAX_ANSWERS));

        RuleFor(x => x.Answers)
            .Must(answers => answers is null
                             || answers.Select(a => a.QuestionId).Distinct().Count() == answers.Count)
            .WithError(ProgressErrors.QuizAttemptDuplicateQuestionIds());

        RuleFor(x => x.AnonymousId)
            .Must(value => Guid.TryParse(value, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.AnonymousId))
            .WithError(GeneralErrors.ValueIsInvalid(nameof(SubmitLevelTestAttemptCommand.AnonymousId)));
    }
}

public sealed class SubmitLevelTestAttemptEndpoint : IEndpoint
{
    public const string RATE_LIMIT_POLICY = "level-test-submit";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Публичная лид-воронка — анонимный сабмит намеренно (Tier-3 не нужен:
        // LEVEL_TEST-квиз — открытый контент, см. ST-3 anonymous GET). Anti-abuse —
        // отдельная rate-limit policy 10/min на IP.
        app.MapPost("/progress/level-test/attempts",
                async Task<EndpointResult<object>> (
                    SubmitLevelTestAttemptRequest request,
                    SubmitLevelTestAttemptHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new SubmitLevelTestAttemptCommand(request.QuizId, request.AnonymousId, request.Answers),
                        cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(RATE_LIMIT_POLICY);
    }
}

/// <summary>
///     Сабмит попытки level-test'а (ST-4, #479). Поток: answer-key из ECS →
///     проверка Purpose=LEVEL_TEST → детерминированный грейдинг в фабрике агрегата
///     (choice + очки по сложности + секции/уровень/рекомендация) → persist. Если есть
///     отвеченные open_text — статус QUEUED и в outbox публикуется локальный
///     <see cref="GradeLevelTestAttemptRequested"/> (handler — ST-5). Ответ lead-gated:
///     аноним получает ТОЛЬКО тизер (общий процент/уровень — отдельный DTO без секций
///     и разбора), залогиненный — полный результат. Ответы на вопросы вне answer-key
///     отбрасываются.
/// </summary>
public sealed class SubmitLevelTestAttemptHandler : ICommandHandler<object, SubmitLevelTestAttemptCommand>
{
    private const string LEVEL_TEST_PURPOSE = "LEVEL_TEST";

    private readonly IValidator<SubmitLevelTestAttemptCommand> _validator;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly ILevelTestAttemptRepository _levelTestAttemptRepository;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly ProgressMetrics _metrics;
    private readonly ILogger<SubmitLevelTestAttemptHandler> _logger;

    public SubmitLevelTestAttemptHandler(
        IValidator<SubmitLevelTestAttemptCommand> validator,
        IEducationContentServiceClient educationContentServiceClient,
        ILevelTestAttemptRepository levelTestAttemptRepository,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        UserScopedData user,
        ProgressMetrics metrics,
        ILogger<SubmitLevelTestAttemptHandler> logger)
    {
        _validator = validator;
        _educationContentServiceClient = educationContentServiceClient;
        _levelTestAttemptRepository = levelTestAttemptRepository;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _user = user;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Result<object, Error>> Handle(
        SubmitLevelTestAttemptCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Сервисные токены (UserId = Guid.Empty) идут по анонимной ветке.
        Guid? userId = _user.IsAuthenticated && _user.UserId != Guid.Empty ? _user.UserId : null;

        Guid? anonymousId = null;
        if (userId is null)
        {
            if (string.IsNullOrWhiteSpace(command.AnonymousId))
            {
                return ProgressErrors.LevelTestAttemptAnonymousIdRequired();
            }

            anonymousId = Guid.Parse(command.AnonymousId);
        }

        Result<QuizAnswerKeyDto, Error> answerKeyResult =
            await _educationContentServiceClient.GetQuizAnswerKeyAsync(command.QuizId, cancellationToken);
        if (answerKeyResult.IsNotFound())
        {
            return answerKeyResult.Error;
        }

        if (answerKeyResult.IsFailure)
        {
            return ProgressErrors.EducationContentServiceUnavailable();
        }

        QuizAnswerKeyDto answerKey = answerKeyResult.Value;
        if (!string.Equals(answerKey.Purpose, LEVEL_TEST_PURPOSE, StringComparison.Ordinal))
        {
            return ProgressErrors.LevelTestAttemptQuizPurposeInvalid();
        }

        Result<IReadOnlyList<LevelTestAnswer>, Error> answersResult =
            BuildAnswers(command.Answers, answerKey);
        if (answersResult.IsFailure)
        {
            return answersResult.Error;
        }

        Result<LevelTestAttempt, Error> attemptResult = LevelTestAttempt.Create(
            command.QuizId,
            userId,
            anonymousId,
            answersResult.Value,
            answerKey.Questions
                .Select(q => new LevelTestQuestionKey(
                    q.Id, q.Type, q.Section, q.Difficulty, q.CorrectOptionIds, q.ReferenceAnswer,
                    (q.Options ?? []).Select(o => new LevelTestOptionKey(o.Id, o.Text)).ToList(),
                    q.Explanation))
                .ToList(),
            answerKey.LevelTestConfig?.Sections
                .Select(s => new LevelTestSectionDefinition(s.Key, s.Title, s.Weight, s.RecommendedCourseId))
                .ToList() ?? [],
            answerKey.LevelTestConfig?.LevelThresholds
                .Select(t => new LevelTestLevelThreshold(t.Level, t.MinPercent))
                .ToList() ?? [],
            answerKey.LevelTestConfig?.FallbackCourseId);
        if (attemptResult.IsFailure)
        {
            return attemptResult.Error;
        }

        LevelTestAttempt attempt = attemptResult.Value;
        await _levelTestAttemptRepository.AddAsync(attempt, cancellationToken);

        if (attempt.AiGradingStatus == LevelTestAiGradingStatus.QUEUED)
        {
            await _outboxService.PublishAsync(new GradeLevelTestAttemptRequested(attempt.Id));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        // Funnel-метрика (#482): персистнутый сабмит, segment по типу субъекта.
        _metrics.IncrementLevelTestSubmitted(authenticated: userId is not null);

        _logger.LogInformation(
            "Level-test attempt submitted. AttemptId: {AttemptId}, QuizId: {QuizId}, UserId: {UserId}, " +
            "OverallPercent: {OverallPercent}, Level: {Level}, AiGradingStatus: {AiGradingStatus}",
            attempt.Id,
            attempt.QuizId,
            userId,
            attempt.OverallPercent,
            attempt.Level,
            attempt.AiGradingStatus);

        // Lead-gate: аноним видит только тизер, полный разбор — после клейма.
        object response = userId is not null
            ? LevelTestResultMapper.BuildFullResult(attempt)
            : (object)LevelTestResultMapper.BuildTeaser(attempt);

        return response;
    }

    private static Result<IReadOnlyList<LevelTestAnswer>, Error> BuildAnswers(
        IReadOnlyList<SubmitLevelTestAnswerItem> items,
        QuizAnswerKeyDto answerKey)
    {
        HashSet<Guid> knownQuestionIds = answerKey.Questions.Select(q => q.Id).ToHashSet();

        var answers = new List<LevelTestAnswer>(items.Count);
        foreach (SubmitLevelTestAnswerItem item in items)
        {
            Result<LevelTestAnswer, Error> answerResult = LevelTestAnswer.Create(
                item.QuestionId,
                item.SelectedOptionIds,
                item.TextAnswer);
            if (answerResult.IsFailure)
            {
                return answerResult.Error;
            }

            if (knownQuestionIds.Contains(answerResult.Value.QuestionId))
            {
                answers.Add(answerResult.Value);
            }
        }

        return answers;
    }
}
