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
///     <c>POST /telegram/me/resync-invites</c> — user-facing «пересинхронизировать
///     инвайты в чаты». Альтернатива event-handler'у на <c>user.telegram_linked</c>:
///     юзер мог записаться на курс позже, или binding появился позже, или бот лежал —
///     этот эндпоинт пере-разошлёт DM-инвайты для всех курсов с активным STANDARD
///     enrollment'ом и bound chat'ом, где он ещё не состоит.
/// </summary>
public sealed record ResyncMyInvitesCommand : ICommand;

public sealed class ResyncMyInvitesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/telegram/me/resync-invites/",
                async Task<EndpointResult<ResyncInvitesResponse>> (
                    [FromServices] ResyncMyInvitesHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new ResyncMyInvitesCommand(), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW)
            .RequireRateLimiting("telegram-link");
    }
}

public sealed class ResyncMyInvitesHandler : ICommandHandler<ResyncInvitesResponse, ResyncMyInvitesCommand>
{
    private readonly IUserLinkRepository _userLinks;
    private readonly TelegramInviteResyncService _resync;
    private readonly UserScopedData _user;
    private readonly ILogger<ResyncMyInvitesHandler> _logger;

    public ResyncMyInvitesHandler(
        IUserLinkRepository userLinks,
        TelegramInviteResyncService resync,
        UserScopedData user,
        ILogger<ResyncMyInvitesHandler> logger)
    {
        _userLinks = userLinks;
        _resync = resync;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<ResyncInvitesResponse, Error>> Handle(
        ResyncMyInvitesCommand command, CancellationToken cancellationToken)
    {
        // .RequirePermissions(...) гарантирует authenticated user → UserId != Guid.Empty.
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            ul => ul.PlatformUserId == _user.UserId, cancellationToken);

        if (linkResult.IsFailure)
        {
            // Telegram не привязан — это не ошибка, просто инвайты слать некуда.
            return new ResyncInvitesResponse(InvitesSent: 0, TelegramLinked: false);
        }

        UserLink link = linkResult.Value;

        // Soft-blocked link → resync would send to a chat that already blocked the bot,
        // triggering 403 swallowed as warnings. Skip with an explicit response field.
        if (!link.IsActive)
        {
            _logger.LogInformation(
                "Resync invites by user request: user {UserId} link is soft-blocked, skip",
                _user.UserId);
            return new ResyncInvitesResponse(InvitesSent: 0, TelegramLinked: true);
        }

        Result<int, Error> resyncResult = await _resync.ResyncInvitesAsync(
            link.PlatformUserId, link.TelegramUserId, cancellationToken);
        if (resyncResult.IsFailure)
            return resyncResult.Error;

        int sent = resyncResult.Value;

        _logger.LogInformation(
            "Resync invites by user request: user {UserId} → {Count} DMs",
            _user.UserId, sent);

        return new ResyncInvitesResponse(InvitesSent: sent, TelegramLinked: true);
    }
}
