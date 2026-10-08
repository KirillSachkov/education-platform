using Core.Abstractions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Quizzes.Queries;

public sealed record GetAuthorQuizQuery(Guid QuizId) : IQuery;

public sealed class GetAuthorQuizEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("quizzes/{quizId:guid}", async Task<EndpointResult<QuizAuthorDto>> (
                    [FromRoute] Guid quizId,
                    [FromServices] GetAuthorQuizHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetAuthorQuizQuery(quizId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Полная авторская проекция квиза — ВКЛЮЧАЯ CorrectOptionIds и ReferenceAnswer.
///     Только владелец (admin/moderator — bypass через CheckOwnership): ответы наружу
///     не отдаются никому, кроме автора.
/// </summary>
public sealed class GetAuthorQuizHandler : IQueryHandlerWithResult<QuizAuthorDto, GetAuthorQuizQuery>
{
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly UserScopedData _userScopedData;

    public GetAuthorQuizHandler(
        IQuizzesRepository quizzesRepository,
        UserScopedData userScopedData)
    {
        _quizzesRepository = quizzesRepository;
        _userScopedData = userScopedData;
    }

    public async Task<Result<QuizAuthorDto, Error>> Handle(
        GetAuthorQuizQuery query,
        CancellationToken cancellationToken)
    {
        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == query.QuizId, cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.QuizNotFound(query.QuizId);

        Quiz quiz = quizResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(quiz.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        return QuizDtoMapper.ToAuthorDto(quiz);
    }
}
