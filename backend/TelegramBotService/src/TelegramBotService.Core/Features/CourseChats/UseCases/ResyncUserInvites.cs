using Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

/// <summary>
///     <c>POST /telegram/admin/users/{userId}/resync-invites/</c> — admin/support-вариант
///     <see cref="ResyncMyInvitesCommand"/> для произвольного пользователя. Резолвит
///     <see cref="UserLink"/> (нет → <c>{ invitesSent: 0, telegramLinked: false }</c>) и
///     зовёт тот же <see cref="TelegramInviteResyncService.ResyncInvitesAsync"/>, что и
///     user-facing endpoint. Issue #444.
/// </summary>
public sealed record ResyncUserInvitesCommand(Guid UserId) : ICommand;

public sealed class ResyncUserInvitesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/telegram/admin/users/{userId:guid}/resync-invites/",
                async Task<EndpointResult<ResyncInvitesResponse>> (
                    [FromRoute] Guid userId,
                    [FromServices] ResyncUserInvitesHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new ResyncUserInvitesCommand(userId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR)
            .RequireRateLimiting("telegram-link");
    }
}

public sealed class ResyncUserInvitesHandler
    : ICommandHandler<ResyncInvitesResponse, ResyncUserInvitesCommand>
{
    private readonly IUserLinkRepository _userLinks;
    private readonly TelegramInviteResyncService _resync;
    private readonly UserScopedData _user;
    private readonly ILogger<ResyncUserInvitesHandler> _logger;

    public ResyncUserInvitesHandler(
        IUserLinkRepository userLinks,
        TelegramInviteResyncService resync,
        UserScopedData user,
        ILogger<ResyncUserInvitesHandler> logger)
    {
        _userLinks = userLinks;
        _resync = resync;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<ResyncInvitesResponse, Error>> Handle(
        ResyncUserInvitesCommand command, CancellationToken cancellationToken)
    {
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            ul => ul.PlatformUserId == command.UserId, cancellationToken);

        if (linkResult.IsFailure)
        {
            // Telegram не привязан — это не ошибка, просто инвайты слать некуда.
            LogAction(command, invitesSent: 0, telegramLinked: false);
            return new ResyncInvitesResponse(InvitesSent: 0, TelegramLinked: false);
        }

        UserLink link = linkResult.Value;

        // Soft-blocked link → resync слал бы в чат, который уже заблокировал бота (403,
        // глотается warning'ами). Пропускаем с явным полем ответа — как user-facing endpoint.
        if (!link.IsActive)
        {
            LogAction(command, invitesSent: 0, telegramLinked: true);
            return new ResyncInvitesResponse(InvitesSent: 0, TelegramLinked: true);
        }

        Result<int, Error> resyncResult = await _resync.ResyncInvitesAsync(
            link.PlatformUserId, link.TelegramUserId, cancellationToken);
        if (resyncResult.IsFailure)
            return resyncResult.Error;

        int sent = resyncResult.Value;

        LogAction(command, invitesSent: sent, telegramLinked: true);
        return new ResyncInvitesResponse(InvitesSent: sent, TelegramLinked: true);
    }

    private void LogAction(ResyncUserInvitesCommand command, int invitesSent, bool telegramLinked) =>
        // resync охватывает все планы пользователя — конкретного PlanId здесь нет,
        // поэтому в audit-строке его не выставляем (иначе plan=Guid.Empty шумит в Loki).
        _logger.LogInformation(
            "support-action {Action} admin={AdminUserId} target={TargetUserId} outcome={Outcome}",
            "resync.invites", _user.UserId, command.UserId,
            $"invitesSent={invitesSent};telegramLinked={telegramLinked}");
}
