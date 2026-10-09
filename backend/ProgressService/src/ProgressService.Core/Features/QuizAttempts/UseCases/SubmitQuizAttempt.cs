using ContentAccess;
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
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Core.Features.QuizAttempts.UseCases;

public sealed record SubmitQuizAttemptCommand(
    Guid QuizId,
    IReadOnlyList<SubmitQuizAnswerItem> Answers) : ICommand;

public sealed class SubmitQuizAttemptCommandValidator : AbstractValidator<SubmitQuizAttemptCommand>
{
    public const int MAX_ANSWERS = 100;

    public SubmitQuizAttemptCommandValidator()
    {
        RuleFor(x => x.QuizId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SubmitQuizAttemptCommand.QuizId)));

        RuleFor(x => x.Answers)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(SubmitQuizAttemptCommand.Answers)));

        RuleFor(x => x.Answers)
            .Must(answers => answers is null || answers.Count <= MAX_ANSWERS)
            .WithError(ProgressErrors.QuizAttemptTooManyAnswers(MAX_ANSWERS));

        RuleFor(x => x.Answers)
            .Must(answers => answers is null
                             || answers.Select(a => a.QuestionId).Distinct().Count() == answers.Count)
            .WithError(ProgressErrors.QuizAttemptDuplicateQuestionIds());
    }
}

public sealed class SubmitQuizAttemptEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/quizzes/{quizId:guid}/attempts",
                async Task<EndpointResult<QuizAttemptResultResponse>> (
                    Guid quizId,
                    SubmitQuizAttemptRequest request,
                    SubmitQuizAttemptHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new SubmitQuizAttemptCommand(quizId, request.Answers),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>Проверяет доступ к квизу, оценивает ответы и сохраняет попытку через ITransactionManager.
///     Успешная попытка завершает Quiz-элемент модуля через доменное событие.
///     Ответ содержит полный разбор; несовпадающие идентификаторы вопросов дают 409.</summary>
public sealed class SubmitQuizAttemptHandler : ICommandHandler<QuizAttemptResultResponse, SubmitQuizAttemptCommand>
{
    private const string PUBLIC_ACCESS_TYPE = "PUBLIC";

    private readonly IValidator<SubmitQuizAttemptCommand> _validator;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IQuizAttemptRepository _quizAttemptRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly ILogger<SubmitQuizAttemptHandler> _logger;

    public SubmitQuizAttemptHandler(
        IValidator<SubmitQuizAttemptCommand> validator,
        IEducationContentServiceClient educationContentServiceClient,
        IEntitlementChecker entitlementChecker,
        IQuizAttemptRepository quizAttemptRepository,
        ITransactionManager transactionManager,
        UserScopedData user,
        ILogger<SubmitQuizAttemptHandler> logger)
    {
        _validator = validator;
        _educationContentServiceClient = educationContentServiceClient;
        _entitlementChecker = entitlementChecker;
        _quizAttemptRepository = quizAttemptRepository;
        _transactionManager = transactionManager;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<QuizAttemptResultResponse, Error>> Handle(
        SubmitQuizAttemptCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
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

        if (!string.Equals(answerKey.AccessType, PUBLIC_ACCESS_TYPE, StringComparison.Ordinal))
        {
            AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
                _user.ToAccessSubject(),
                ResourceTypes.QUIZ,
                command.QuizId,
                cancellationToken);
            if (!accessDecision.IsGranted)
            {
                return ProgressErrors.QuizAccessDenied();
            }
        }

        HashSet<Guid> knownQuestionIds = answerKey.Questions.Select(q => q.Id).ToHashSet();

        // Fail loud: автор отредактировал квиз → ECS перегенерировал id вопросов/вариантов.
        // Студент, загрузивший квиз ДО правки, сабмитит со старыми id — все ответы молча
        // дропнулись бы (unknown-question filter) → 0% / «всё красное». Если в запросе есть
        // ответы, но НИ ОДИН их questionId не совпадает с актуальным ключом — квиз сменился
        // под студентом. Возвращаем 409 и не персистим попытку. Пустой сабмит (0 ответов) —
        // легитимная пустая попытка (0%), не триггерит ошибку.
        if (command.Answers.Count > 0 && !command.Answers.Any(a => knownQuestionIds.Contains(a.QuestionId)))
        {
            return ProgressErrors.QuizChangedReload();
        }

        Result<IReadOnlyList<QuizAttemptAnswer>, Error> answersResult =
            BuildAnswers(command.Answers, knownQuestionIds);
        if (answersResult.IsFailure)
        {
            return answersResult.Error;
        }

        QuizAttemptGrading grading = QuizAttemptGrader.Grade(answerKey, answersResult.Value);

        Result<QuizAttempt, Error> attemptResult = QuizAttempt.Create(
            _user.UserId,
            command.QuizId,
            answersResult.Value,
            grading.ScorePercent,
            grading.Passed);
        if (attemptResult.IsFailure)
        {
            return attemptResult.Error;
        }

        QuizAttempt attempt = attemptResult.Value;
        await _quizAttemptRepository.AddAsync(attempt, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Quiz attempt submitted. UserId: {UserId}, QuizId: {QuizId}, Score: {ScorePercent}, Passed: {Passed}",
            _user.UserId,
            command.QuizId,
            attempt.ScorePercent,
            attempt.Passed);

        return QuizAttemptGrader.BuildResult(attempt, answerKey);
    }

    private static Result<IReadOnlyList<QuizAttemptAnswer>, Error> BuildAnswers(
        IReadOnlyList<SubmitQuizAnswerItem> items,
        HashSet<Guid> knownQuestionIds)
    {
        var answers = new List<QuizAttemptAnswer>(items.Count);
        foreach (SubmitQuizAnswerItem item in items)
        {
            Result<QuizAttemptAnswer, Error> answerResult = QuizAttemptAnswer.Create(
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