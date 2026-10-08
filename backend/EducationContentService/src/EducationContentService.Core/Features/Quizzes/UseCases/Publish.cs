using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Quizzes.UseCases;

public sealed record PublishQuizCommand(Guid QuizId) : ICommand;

public sealed class PublishQuizEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("quizzes/{quizId:guid}/publish", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid quizId,
                    [FromServices] PublishQuizHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new PublishQuizCommand(quizId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Публикует квиз. Доменный инвариант: хотя бы один вопрос
///     (<see cref="Quiz.Publish"/>). Публикует <c>quiz.published</c> (#490) —
///     self-consume handler выставляет Redis-теги доступа (теги существуют
///     только у PUBLISHED-квизов, точка появления — именно publish).
/// </summary>
public sealed class PublishQuizHandler : ICommandHandler<Guid, PublishQuizCommand>
{
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ICourseQuizzesRepository _courseQuizzesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<PublishQuizHandler> _logger;

    public PublishQuizHandler(
        IQuizzesRepository quizzesRepository,
        ICourseQuizzesRepository courseQuizzesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        UserScopedData userScopedData,
        ILogger<PublishQuizHandler> logger)
    {
        _quizzesRepository = quizzesRepository;
        _courseQuizzesRepository = courseQuizzesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        PublishQuizCommand command,
        CancellationToken cancellationToken)
    {
        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == command.QuizId, cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.QuizNotFound(command.QuizId);

        Quiz quiz = quizResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(quiz.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> publishResult = quiz.Publish();
        if (publishResult.IsFailure)
            return publishResult.Error;

        List<Guid> courseIds = await _courseQuizzesRepository.GetCourseIdsAsync(quiz.Id, cancellationToken);

        await _outbox.PublishAsync(new QuizPublished(
            QuizId: quiz.Id,
            AuthorId: quiz.AuthorId,
            AccessType: quiz.AccessType.ToString(),
            CourseIds: courseIds));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Quiz {QuizId} published", quiz.Id);

        return quiz.Id;
    }
}
