using ContentAccess;
using Core.Abstractions;
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
using ProgressService.Core.Extensions;
using ProgressService.Domain;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Core.Features.QuizAttempts.UseCases;

public sealed record CheckQuizQuestionCommand(
    Guid QuizId,
    Guid QuestionId,
    IReadOnlyList<Guid>? SelectedOptionIds,
    string? TextAnswer) : ICommand;

public sealed class CheckQuizQuestionCommandValidator : AbstractValidator<CheckQuizQuestionCommand>
{
    public CheckQuizQuestionCommandValidator()
    {
        RuleFor(x => x.QuizId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CheckQuizQuestionCommand.QuizId)));

        RuleFor(x => x.QuestionId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CheckQuizQuestionCommand.QuestionId)));

        RuleFor(x => x.SelectedOptionIds)
            .Must(ids => ids is null || ids.Count <= QuizAttemptAnswer.MAX_SELECTED_OPTIONS)
            .WithError(ProgressErrors.QuizAttemptTooManySelectedOptions(QuizAttemptAnswer.MAX_SELECTED_OPTIONS));

        RuleFor(x => x.TextAnswer)
            .Must(text => text is null || text.Length <= QuizAttemptAnswer.MAX_TEXT_ANSWER_LENGTH)
            .WithError(ProgressErrors.QuizAttemptTextAnswerTooLong(QuizAttemptAnswer.MAX_TEXT_ANSWER_LENGTH));
    }
}

public sealed class CheckQuizQuestionEndpoint : IEndpoint
{
    /// <summary>
    ///     Per-user rate-limit «Проверить ответ» (#556): дешевле сабмита (один поход за
    ///     answer-key + грейдинг одного вопроса, без записи), но дёргается часто — студент
    ///     проверяет каждый вопрос. Партиционируем по sub-claim, 120/min.
    /// </summary>
    public const string RATE_LIMIT_POLICY = "quiz-question-check";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/quizzes/{quizId:guid}/questions/{questionId:guid}/check",
                async Task<EndpointResult<CheckQuizQuestionResponse>> (
                    Guid quizId,
                    Guid questionId,
                    CheckQuizQuestionRequest request,
                    CheckQuizQuestionHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new CheckQuizQuestionCommand(
                            quizId,
                            questionId,
                            request.SelectedOptionIds,
                            request.TextAnswer),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW)
            .RequireRateLimiting(RATE_LIMIT_POLICY);
    }
}

/// <summary>Проверяет доступ к квизу и вычисляет результат одного ответа без сохранения попытки.</summary>
public sealed class CheckQuizQuestionHandler : ICommandHandler<CheckQuizQuestionResponse, CheckQuizQuestionCommand>
{
    private const string PUBLIC_ACCESS_TYPE = "PUBLIC";

    private readonly IValidator<CheckQuizQuestionCommand> _validator;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _user;

    public CheckQuizQuestionHandler(
        IValidator<CheckQuizQuestionCommand> validator,
        IEducationContentServiceClient educationContentServiceClient,
        IEntitlementChecker entitlementChecker,
        UserScopedData user)
    {
        _validator = validator;
        _educationContentServiceClient = educationContentServiceClient;
        _entitlementChecker = entitlementChecker;
        _user = user;
    }

    public async Task<Result<CheckQuizQuestionResponse, Error>> Handle(
        CheckQuizQuestionCommand command,
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

        QuizAnswerKeyQuestionDto? question =
            answerKey.Questions.FirstOrDefault(q => q.Id == command.QuestionId);
        if (question is null)
        {
            return ProgressErrors.QuizQuestionNotFound();
        }

        bool? correct = QuizAttemptGrader.GradeOne(question, command.SelectedOptionIds, command.TextAnswer);

        IReadOnlyList<QuizAttemptOptionResultResponse> options =
            (question.Options ?? [])
                .Select(o => new QuizAttemptOptionResultResponse(o.Id, o.Text))
                .ToList();

        return new CheckQuizQuestionResponse(
            question.Id,
            question.Type,
            correct,
            question.CorrectOptionIds,
            question.ReferenceAnswer,
            options,
            question.Explanation);
    }
}