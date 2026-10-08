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
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

/// <summary>
///     <c>GET /telegram/admin/plans/{planId}/chats/</c> — admin/support view всех
///     chat-binding'ов плана (без дедупа — отдаём каждый binding как есть). Пустой план →
///     <c>{ chats: [] }</c> (200). Issue #444.
/// </summary>
public sealed record GetAdminPlanChatsQuery(Guid PlanId) : IQuery;

public sealed class GetAdminPlanChatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/telegram/admin/plans/{planId:guid}/chats/",
                async Task<EndpointResult<AdminPlanChatsDto>> (
                    [FromRoute] Guid planId,
                    [FromServices] GetAdminPlanChatsHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetAdminPlanChatsQuery(planId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
    }
}

public sealed class GetAdminPlanChatsHandler
    : IQueryHandlerWithResult<AdminPlanChatsDto, GetAdminPlanChatsQuery>
{
    private readonly IChatBindingRepository _bindings;

    public GetAdminPlanChatsHandler(IChatBindingRepository bindings)
    {
        _bindings = bindings;
    }

    public async Task<Result<AdminPlanChatsDto, Error>> Handle(
        GetAdminPlanChatsQuery query, CancellationToken ct)
    {
        Guid planId = query.PlanId;
        IReadOnlyList<ChatBinding> bindings = await _bindings.GetManyByAsync(
            x => x.PlanId == planId, ct);

        IReadOnlyList<AdminPlanChatDto> chats = bindings
            .Select(b => new AdminPlanChatDto(
                b.TelegramChatId,
                b.ChatTitle,
                b.ChatType.ToString(),
                b.InviteLink,
                b.EnrollmentGrantsMembership))
            .ToList();

        return new AdminPlanChatsDto(chats);
    }
}
