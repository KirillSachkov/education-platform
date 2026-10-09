using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Quizzes.UseCases;

public sealed record UpdateQuizCommand(Guid QuizId, UpdateQuizRequest Request) : ICommand;

public sealed class UpdateQuizRequestValidator : AbstractValidator<UpdateQuizRequest>
{
    public UpdateQuizRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);

        RuleFor(x => x.PassingScorePercent)
            .InclusiveBetween(0, 100)
            .WithError(EducationErrors.InvalidQuizPassingScore());

        RuleFor(x => x.Questions!)
            .Must(q => q.Count <= Quiz.MAX_QUESTIONS)
            .WithError(EducationErrors.QuizQuestionsLimitExceeded(Quiz.MAX_QUESTIONS))
            .When(x => x.Questions is not null);

        RuleFor(x => x.AccessType!)
            .Must(value => Enum.TryParse<AccessType>(value, ignoreCase: true, out _))
            .WithError(EducationErrors.InvalidAccessType())
            .When(x => !string.IsNullOrWhiteSpace(x.AccessType));
    }
}

public sealed class UpdateQuizEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("quizzes/{quizId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid quizId,
                    [FromBody] UpdateQuizRequest request,
                    [FromServices] UpdateQuizHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UpdateQuizCommand(quizId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>Обновляет standalone-квиз; заменяет вопросы целиком.
///     Проверяет ownership и сохраняет изменение доступа через outbox.</summary>
public sealed class UpdateQuizHandler : ICommandHandler<Guid, UpdateQuizCommand>
{
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ICourseQuizzesRepository _courseQuizzesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<UpdateQuizRequest> _validator;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<UpdateQuizHandler> _logger;

    public UpdateQuizHandler(
        IQuizzesRepository quizzesRepository,
        ICourseQuizzesRepository courseQuizzesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<UpdateQuizRequest> validator,
        UserScopedData userScopedData,
        ILogger<UpdateQuizHandler> logger)
    {
        _quizzesRepository = quizzesRepository;
        _courseQuizzesRepository = courseQuizzesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateQuizCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == command.QuizId, cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.QuizNotFound(command.QuizId);

        Quiz quiz = quizResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(quiz.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Title title = Title.Create(command.Request.Title).Value;

        Result<List<QuizQuestion>, Error> questionsResult = QuizQuestionMapper.Map(command.Request.Questions);
        if (questionsResult.IsFailure)
            return questionsResult.Error;

        bool titleConflict = await _quizzesRepository.ExistsByTitleAsync(
            title, excludeId: quiz.Id, cancellationToken);
        if (titleConflict)
            return EducationErrors.TitleAlreadyExists("Quiz", title.Value);

        // null в запросе — не менять текущий уровень доступа.
        AccessType accessType = string.IsNullOrWhiteSpace(command.Request.AccessType)
            ? quiz.AccessType
            : Enum.Parse<AccessType>(command.Request.AccessType, ignoreCase: true);

        AccessType previousAccessType = quiz.AccessType;

        UnitResult<Error> updateResult = quiz.Update(
            title, command.Request.PassingScorePercent, accessType);
        if (updateResult.IsFailure)
            return updateResult.Error;

        UnitResult<Error> updateQuestionsResult = quiz.UpdateQuestions(questionsResult.Value);
        if (updateQuestionsResult.IsFailure)
            return updateQuestionsResult.Error;

        if (previousAccessType != accessType)
        {
            List<Guid> courseIds = await _courseQuizzesRepository.GetCourseIdsAsync(quiz.Id, cancellationToken);

            await _outbox.PublishAsync(new QuizAccessChanged(
                quiz.Id,
                accessType.ToString(),
                courseIds,
                quiz.AuthorId));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Quiz {QuizId} updated (Questions={QuestionCount}, PassingScore={PassingScore})",
            quiz.Id, quiz.Questions.Count, quiz.PassingScorePercent);

        return quiz.Id;
    }
}