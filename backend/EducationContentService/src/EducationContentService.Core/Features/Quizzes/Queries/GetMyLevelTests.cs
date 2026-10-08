using Core.Abstractions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Quizzes.Queries;

public sealed record GetMyLevelTestsQuery : IQuery;

public sealed class GetMyLevelTestsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("quizzes/level-test/mine", async Task<EndpointResult<IReadOnlyList<QuizAuthorDto>>> (
                    [FromServices] GetMyLevelTestsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMyLevelTestsQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Авторский rediscovery level-test'ов: все квизы caller'а с
///     <see cref="QuizPurpose.LEVEL_TEST"/> в ЛЮБОМ статусе (включая DRAFT) — странице
///     «Тест уровня» после reload нужно снова найти свой тест. Проекция полная
///     (<see cref="QuizAuthorDto"/> — с ответами, section/difficulty и LevelTestConfig),
///     поэтому область видимости зеркалит Tier-2 ownership-семантику
///     <c>OwnershipExtensions.CheckOwnership</c>: автор видит только свои, admin /
///     content-moderator — все. Порядок — новые сверху. Пустой список — тестов ещё нет
///     (фронт показывает CTA создания). Issue #487.
/// </summary>
public sealed class GetMyLevelTestsHandler
    : IQueryHandlerWithResult<IReadOnlyList<QuizAuthorDto>, GetMyLevelTestsQuery>
{
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly UserScopedData _userScopedData;

    public GetMyLevelTestsHandler(
        IQuizzesRepository quizzesRepository,
        UserScopedData userScopedData)
    {
        _quizzesRepository = quizzesRepository;
        _userScopedData = userScopedData;
    }

    public async Task<Result<IReadOnlyList<QuizAuthorDto>, Error>> Handle(
        GetMyLevelTestsQuery query,
        CancellationToken cancellationToken)
    {
        bool seesAll = _userScopedData.IsAdmin
            || _userScopedData.HasPermission(PlatformPermissions.Content.MODERATE);
        Guid userId = _userScopedData.UserId;

        IReadOnlyList<Quiz> quizzes = await _quizzesRepository.GetManyByAsync(
            q => q.Purpose == QuizPurpose.LEVEL_TEST && (seesAll || q.AuthorId == userId),
            cancellationToken);

        return quizzes
            .OrderByDescending(q => q.CreatedAt)
            .ThenByDescending(q => q.Id)
            .Select(QuizDtoMapper.ToAuthorDto)
            .ToList();
    }
}
