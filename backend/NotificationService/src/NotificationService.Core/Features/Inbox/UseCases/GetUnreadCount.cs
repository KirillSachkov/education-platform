using Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Inbox.Dtos;
using NotificationService.Core.Database;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.Inbox.UseCases;

public sealed class GetUnreadCountEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/unread-count", async Task<EndpointResult<UnreadCountResponse>> (
                [FromServices] GetUnreadCountHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new GetUnreadCountQuery(), cancellationToken))
            .RequireAuthorization();
    }
}

public sealed record GetUnreadCountQuery : IQuery;

public sealed class GetUnreadCountHandler : IQueryHandlerWithResult<UnreadCountResponse, GetUnreadCountQuery>
{
    private readonly INotificationsRepository _notificationsRepository;
    private readonly UserScopedData _user;

    public GetUnreadCountHandler(
        INotificationsRepository notificationsRepository,
        UserScopedData user)
    {
        _notificationsRepository = notificationsRepository;
        _user = user;
    }

    public async Task<Result<UnreadCountResponse, Error>> Handle(
        GetUnreadCountQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = _user.UserId;

        int count = await _notificationsRepository.CountBy(
            x => x.RecipientUserId == userId && x.ReadAt == null,
            cancellationToken);

        return new UnreadCountResponse { Count = count };
    }
}
