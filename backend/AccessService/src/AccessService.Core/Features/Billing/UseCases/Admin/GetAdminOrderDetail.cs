using AccessService.Contracts.Billing.Admin;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Billing.UseCases.Admin;

/// <summary>
/// GET /access/admin/orders/{id} — admin/moderator-only detail view: полные поля
/// Order'а + chronological audit-log из <c>order_events</c>.
///
/// Phase F.1.5 (issue #102).
/// </summary>
public sealed class GetAdminOrderDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/access/admin/orders/{id:guid}", async Task<EndpointResult<GetAdminOrderDetailResponse>> (
                Guid id,
                [FromServices] GetAdminOrderDetailHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetAdminOrderDetailQuery(id), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}

public sealed record GetAdminOrderDetailQuery(Guid OrderId) : IQuery;

public sealed class GetAdminOrderDetailHandler
    : IQueryHandlerWithResult<GetAdminOrderDetailResponse, GetAdminOrderDetailQuery>
{
    private readonly IOrdersRepository _orders;
    private readonly IOrderEventsRepository _orderEvents;

    public GetAdminOrderDetailHandler(
        IOrdersRepository orders,
        IOrderEventsRepository orderEvents)
    {
        _orders = orders;
        _orderEvents = orderEvents;
    }

    public async Task<Result<GetAdminOrderDetailResponse, Error>> Handle(
        GetAdminOrderDetailQuery query,
        CancellationToken cancellationToken)
    {
        Result<Order, Error> orderResult = await _orders.GetByAsync(
            o => o.Id == query.OrderId, cancellationToken);
        if (orderResult.IsFailure)
        {
            return orderResult.Error;
        }

        Order order = orderResult.Value;

        IReadOnlyList<OrderEvent> events = await _orderEvents.GetByOrderIdAsync(
            order.Id, cancellationToken);

        AdminOrderDetail detail = new(
            order.Id,
            order.UserId,
            order.PlanId,
            order.AmountCents,
            order.Currency,
            order.Status.ToString(),
            order.Provider,
            order.ExternalProviderRef,
            order.CreatedAt,
            order.PaidAt,
            order.FailureReason,
            order.CorrelationId);

        IReadOnlyList<AdminOrderEventDto> eventDtos = events
            .Select(e => new AdminOrderEventDto(
                e.Id,
                e.EventType.ToString(),
                e.PayloadJson,
                e.ActorUserId,
                e.CreatedAt,
                e.CorrelationId))
            .ToList();

        return new GetAdminOrderDetailResponse(detail, eventDtos);
    }
}
