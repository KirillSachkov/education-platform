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

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

public sealed record HasActiveChatBindingQuery(Guid PlanId) : IQuery;

public sealed class HasActiveChatBindingEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // S2S: вызывается AccessService.SetOnboardingEnabledHandler чтобы при включении
        // онбординга ensure'ить TELEGRAM-шаг если у плана уже есть привязанный чат.
        app.MapGet("/internal/telegram/plans/{planId:guid}/has-active-chat-binding/",
                async Task<EndpointResult<HasActiveChatBindingResponse>> (
                    [FromRoute] Guid planId,
                    [FromServices] HasActiveChatBindingHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new HasActiveChatBindingQuery(planId), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class HasActiveChatBindingHandler
    : IQueryHandlerWithResult<HasActiveChatBindingResponse, HasActiveChatBindingQuery>
{
    private readonly IChatBindingRepository _bindings;

    public HasActiveChatBindingHandler(IChatBindingRepository bindings) => _bindings = bindings;

    public async Task<Result<HasActiveChatBindingResponse, Error>> Handle(
        HasActiveChatBindingQuery query, CancellationToken ct)
    {
        bool exists = await _bindings.ExistsAsync(b => b.PlanId == query.PlanId, ct);
        return new HasActiveChatBindingResponse(exists);
    }
}
