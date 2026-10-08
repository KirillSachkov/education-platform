using AccessService.Core.Database;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.PlanGrants.UseCases;

/// <summary>
/// Service-to-service endpoint: возвращает distinct user IDs с активным
/// FULL_ALL/LEARN_ALL plan-grant'ом. <c>authorId</c> остался в route для обратной
/// совместимости, но полный доступ теперь глобальный и не фильтруется по автору.
///
/// Используется NotificationService при <c>course.created</c>: новый курс автомагически
/// доступен FULL/LEARN-grantee'ам, и они должны автоматически получить subscription
/// на новый курс (иначе material.published в нём не дойдёт до них). Issue #80.
/// </summary>
public sealed class GetLifetimeGranteesByAuthorEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/access/users/with-lifetime/{authorId:guid}", async Task<EndpointResult<IReadOnlyList<Guid>>> (
                [FromRoute] Guid authorId,
                [FromServices] GetLifetimeGranteesByAuthorHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetLifetimeGranteesByAuthorQuery(authorId), ct))
            .RequireAuthorization()
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed record GetLifetimeGranteesByAuthorQuery(Guid AuthorId) : IQuery;

public sealed class GetLifetimeGranteesByAuthorHandler
    : IQueryHandlerWithResult<IReadOnlyList<Guid>, GetLifetimeGranteesByAuthorQuery>
{
    private readonly IPlanGrantsRepository _grants;

    public GetLifetimeGranteesByAuthorHandler(IPlanGrantsRepository grants) => _grants = grants;

    public async Task<Result<IReadOnlyList<Guid>, Error>> Handle(
        GetLifetimeGranteesByAuthorQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> userIds = await _grants.GetActiveLifetimeUserIdsByAuthorAsync(
            query.AuthorId,
            cancellationToken);

        return Result.Success<IReadOnlyList<Guid>, Error>(userIds);
    }
}
