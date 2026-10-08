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

public sealed record ListChatBindingsQuery(Guid PlanId) : IQuery;

public sealed class ListChatBindingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/telegram/admin/plans/{planId:guid}/chat-bindings/",
                async Task<EndpointResult<IReadOnlyList<ChatBindingDto>>> (
                    [FromRoute] Guid planId,
                    [FromServices] ListChatBindingsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new ListChatBindingsQuery(planId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed class ListChatBindingsHandler
    : IQueryHandlerWithResult<IReadOnlyList<ChatBindingDto>, ListChatBindingsQuery>
{
    private readonly IChatBindingRepository _bindings;

    public ListChatBindingsHandler(IChatBindingRepository bindings) => _bindings = bindings;

    public async Task<Result<IReadOnlyList<ChatBindingDto>, Error>> Handle(
        ListChatBindingsQuery query, CancellationToken cancellationToken)
    {
        Guid planId = query.PlanId;
        IReadOnlyList<ChatBinding> rows = await _bindings.GetManyByAsync(
            x => x.PlanId == planId, cancellationToken);

        IReadOnlyList<ChatBindingDto> dtos = rows
            .Select(b => new ChatBindingDto(
                b.Id,
                b.PlanId,
                b.TelegramChatId,
                b.ChatType.ToString(),
                b.ChatTitle,
                b.InviteLink,
                b.EnrollmentGrantsMembership,
                b.MembershipGrantsEnrollment,
                b.AutoKickOnRevoke,
                b.EnforceMembership,
                b.CreatedAt,
                b.AnnouncementMessageId))
            .ToList();

        return Result.Success<IReadOnlyList<ChatBindingDto>, Error>(dtos);
    }
}
