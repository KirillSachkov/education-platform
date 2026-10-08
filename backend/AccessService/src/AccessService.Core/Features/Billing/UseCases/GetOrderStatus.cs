using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>
/// GET /access/orders/{id}/status — статус заказа для polling'а с frontend'а.
/// После redirect'а с T-Bank на /payment/success?orderId=X фронт опрашивает endpoint
/// до терминального статуса (PAID/FAILED/REFUNDED) — fallback на случай, если webhook
/// от T-Bank ещё не дошёл.
///
/// Owner-only: 403 если запрашивающий не владелец Order'а; admin может смотреть всё.
/// </summary>
public sealed class GetOrderStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/orders/{id:guid}/status", async Task<EndpointResult<GetOrderStatusResponse>> (
                Guid id,
                [FromServices] GetOrderStatusHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetOrderStatusQuery(id), ct))
            .RequireAuthorization();
    }
}

public sealed record GetOrderStatusQuery(Guid OrderId) : IQuery;

public sealed class GetOrderStatusHandler : IQueryHandlerWithResult<GetOrderStatusResponse, GetOrderStatusQuery>
{
    private readonly IOrdersRepository _orders;
    private readonly UserScopedData _user;

    public GetOrderStatusHandler(IOrdersRepository orders, UserScopedData user)
    {
        _orders = orders;
        _user = user;
    }

    public async Task<Result<GetOrderStatusResponse, Error>> Handle(
        GetOrderStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<Order, Error> orderResult = await _orders.GetByAsync(
            o => o.Id == query.OrderId,
            cancellationToken);
        if (orderResult.IsFailure)
        {
            return orderResult.Error;
        }

        Order order = orderResult.Value;

        // Owner-only check (admin bypasses).
        if (order.UserId != _user.UserId && !_user.IsAdmin)
        {
            return AccessErrors.AccessDenied();
        }

        // FailureReason — сырой внутренний текст (errorCode'ы, суммы вроде
        // "amount_mismatch: webhook=..., order=..."). Наружу отдаём только
        // нормализованное русское сообщение, без внутренних деталей.
        return new GetOrderStatusResponse(
            order.Id,
            order.Status.ToString(),
            order.PaidAt,
            FailureReasonNormalizer.ToUserFacing(order.FailureReason));
    }
}
