using ContentAccess;
using Core.Abstractions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Quizzes.Queries;

public sealed record GetStudentQuizQuery(Guid QuizId) : IQuery;

public sealed class GetStudentQuizEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("quizzes/{quizId:guid}/student", async Task<EndpointResult<QuizStudentDto>> (
                    [FromRoute] Guid quizId,
                    [FromServices] GetStudentQuizHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetStudentQuizQuery(quizId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

/// <summary>
///     Студенческое standalone-чтение квиза (#490) — БЕЗ материала-посредника.
///     Проекция answer-stripped (<see cref="QuizStudentDto"/> физически не содержит
///     CorrectOptionIds / ReferenceAnswer), <c>MaterialId = null</c>.
///     Отдаёт только PUBLISHED — DRAFT 404 даже владельцу (у владельца авторский
///     <c>GET /quizzes/{id}</c>).
///     Tier-3 — по СОБСТВЕННОМУ <see cref="Quiz.AccessType"/> квиза, зеркало
///     <c>GetMaterialDetail</c>: автор-владелец и PUBLIC — short-circuit по данным БД,
///     остальное — <see cref="IEntitlementChecker"/> (resource type <c>quiz</c>;
///     admin bypass внутри checker'а); аноним без доступа → 401, авторизованный → 403.
/// </summary>
public sealed class GetStudentQuizHandler : IQueryHandlerWithResult<QuizStudentDto, GetStudentQuizQuery>
{
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _userData;
    private readonly ILogger<GetStudentQuizHandler> _logger;

    public GetStudentQuizHandler(
        IQuizzesRepository quizzesRepository,
        IEntitlementChecker entitlementChecker,
        UserScopedData userData,
        ILogger<GetStudentQuizHandler> logger)
    {
        _quizzesRepository = quizzesRepository;
        _entitlementChecker = entitlementChecker;
        _userData = userData;
        _logger = logger;
    }

    public async Task<Result<QuizStudentDto, Error>> Handle(
        GetStudentQuizQuery query, CancellationToken cancellationToken)
    {
        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == query.QuizId && q.Status == PublicationStatus.PUBLISHED,
            cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.QuizNotFound(query.QuizId);

        Quiz quiz = quizResult.Value;

        // Short-circuit по данным БД — зеркалит GetMaterialDetail: PUBLIC — всегда
        // доступен (устойчив к падению Redis), автору — всегда доступен.
        bool isAuthor = _userData.IsAuthenticated && quiz.AuthorId == _userData.UserId;
        bool isPublic = quiz.AccessType == AccessType.PUBLIC;

        if (!isAuthor && !isPublic)
        {
            AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
                _userData.ToAccessSubject(),
                ResourceTypes.QUIZ,
                quiz.Id,
                cancellationToken);

            if (!decision.IsGranted)
            {
                _logger.LogInformation(
                    "Access denied to quiz {QuizId} for user {UserId}",
                    quiz.Id, _userData.UserId);

                return _userData.IsAuthenticated
                    ? EducationErrors.AccessDenied()
                    : EducationErrors.UnauthorizedAccess();
            }
        }

        return QuizDtoMapper.ToStudentDto(quiz, materialId: null);
    }
}
