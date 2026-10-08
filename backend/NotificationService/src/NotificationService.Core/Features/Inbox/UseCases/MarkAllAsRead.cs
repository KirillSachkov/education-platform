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

public sealed class MarkAllAsReadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/read-all", async Task<EndpointResult<MarkAllAsReadResponse>> (
                [FromServices] MarkAllAsReadHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new MarkAllAsReadCommand(), cancellationToken))
            .RequireAuthorization();
    }
}

public sealed record MarkAllAsReadCommand : ICommand;

public sealed class MarkAllAsReadHandler : ICommandHandler<MarkAllAsReadResponse, MarkAllAsReadCommand>
{
    private readonly INotificationsRepository _notificationsRepository;
    private readonly UserScopedData _user;

    public MarkAllAsReadHandler(
        INotificationsRepository notificationsRepository,
        UserScopedData user)
    {
        _notificationsRepository = notificationsRepository;
        _user = user;
    }

    public async Task<Result<MarkAllAsReadResponse, Error>> Handle(
        MarkAllAsReadCommand command,
        CancellationToken cancellationToken)
    {
        int updated = await _notificationsRepository.MarkAllAsReadAsync(_user.UserId, cancellationToken);

        return new MarkAllAsReadResponse { Updated = updated };
    }
}
