using Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

/// <summary>
///     <c>GET /telegram/admin/users/{userId}/link/</c> — admin/support view привязки Telegram
///     для произвольного пользователя. Резолвит <see cref="UserLink"/> по PlatformUserId;
///     нет привязки → <c>{ linked: false, ... }</c> (200, не ошибка). Issue #444.
/// </summary>
public sealed record GetAdminUserTelegramLinkQuery(Guid UserId) : IQuery;

public sealed class GetAdminUserTelegramLinkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/telegram/admin/users/{userId:guid}/link/",
                async Task<EndpointResult<AdminTelegramLinkDto>> (
                    [FromRoute] Guid userId,
                    [FromServices] GetAdminUserTelegramLinkHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetAdminUserTelegramLinkQuery(userId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
    }
}

public sealed class GetAdminUserTelegramLinkHandler
    : IQueryHandlerWithResult<AdminTelegramLinkDto, GetAdminUserTelegramLinkQuery>
{
    private readonly IUserLinkRepository _userLinks;

    public GetAdminUserTelegramLinkHandler(IUserLinkRepository userLinks)
    {
        _userLinks = userLinks;
    }

    public async Task<Result<AdminTelegramLinkDto, Error>> Handle(
        GetAdminUserTelegramLinkQuery query, CancellationToken ct)
    {
        Result<UserLink, Error> linkResult = await _userLinks.GetBy(
            x => x.PlatformUserId == query.UserId, ct);

        if (linkResult.IsFailure)
            return new AdminTelegramLinkDto(Linked: false, TelegramUserId: null,
                TelegramUsername: null, LinkedAt: null);

        UserLink link = linkResult.Value;
        return new AdminTelegramLinkDto(
            Linked: true,
            TelegramUserId: link.TelegramUserId,
            TelegramUsername: link.TelegramUsername,
            LinkedAt: link.LinkedAt);
    }
}
