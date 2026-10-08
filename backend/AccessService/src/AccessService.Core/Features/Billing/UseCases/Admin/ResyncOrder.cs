using AccessService.Contracts.Billing;
using AccessService.Contracts.Billing.Admin;
using AccessService.Core.Database;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Billing.UseCases.Admin;

/// <summary>
/// POST /access/admin/orders/{id}/resync — manual single-order reconciliation.
/// Тот же путь, что и <c>PendingOrderReconciliationService.RunOnceAsync</c>, но
/// для одного заказа: <c>tbank.GetState(externalRef)</c> →
/// <c>TBankStatusMapper.Map</c> → <c>PaymentWebhookHandler.Handle</c>.
///
/// Только для PENDING-заказов или FAILED-заказов, истёкших в reconciliation,
/// с <c>ExternalProviderRef</c>. Возвращает previous + new статусы для UI-фидбэка.
///
/// Phase F.1.5 (issue #102).
/// </summary>
public sealed class ResyncOrderEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/access/admin/orders/{id:guid}/resync", async Task<EndpointResult<ResyncOrderResponse>> (
                Guid id,
                [FromServices] ResyncOrderHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new ResyncOrderCommand(id), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}

public sealed record ResyncOrderCommand(Guid OrderId) : ICommand;

public sealed class ResyncOrderHandler : ICommandHandler<ResyncOrderResponse, ResyncOrderCommand>
{
    private readonly IOrdersRepository _orders;
    private readonly IOrderEventsRepository _orderEvents;
    private readonly ITBankClient _tbank;
    private readonly PaymentWebhookHandler _webhookHandler;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<ResyncOrderHandler> _logger;

    public ResyncOrderHandler(
        IOrdersRepository orders,
        IOrderEventsRepository orderEvents,
        ITBankClient tbank,
        PaymentWebhookHandler webhookHandler,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<ResyncOrderHandler> logger)
    {
        _orders = orders;
        _orderEvents = orderEvents;
        _tbank = tbank;
        _webhookHandler = webhookHandler;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<ResyncOrderResponse, Error>> Handle(
        ResyncOrderCommand command,
        CancellationToken cancellationToken)
    {
        Result<Order, Error> orderResult = await _orders.GetByAsync(
            o => o.Id == command.OrderId, cancellationToken);
        if (orderResult.IsFailure)
        {
            return orderResult.Error;
        }

        Order order = orderResult.Value;
        string previousStatus = order.Status.ToString();

        bool isExpiredReconciliation = order.Status == OrderStatus.FAILED
            && string.Equals(
                order.FailureReason,
                "reconciliation_expired",
                StringComparison.Ordinal);

        if (order.Status != OrderStatus.PENDING && !isExpiredReconciliation)
        {
            return Error.Validation(
                "order.not_pending",
                "Resync доступен только для PENDING-заказов или "
                + "FAILED-заказов с причиной reconciliation_expired; "
                + $"текущий статус: {order.Status}");
        }

        if (string.IsNullOrEmpty(order.ExternalProviderRef))
        {
            return Error.Validation(
                "order.no_external_ref",
                "У заказа нет external_provider_ref — нечего проверять у T-Bank");
        }

        Result<TBankGetStateResponse, Error> stateResult =
            await _tbank.GetStateAsync(order.ExternalProviderRef, cancellationToken);
        if (stateResult.IsFailure)
        {
            return stateResult.Error;
        }

        TBankGetStateResponse state = stateResult.Value;

        bool paymentMatches = string.Equals(
            state.PaymentId,
            order.ExternalProviderRef,
            StringComparison.Ordinal);
        bool orderMatches = Guid.TryParse(state.OrderId, out Guid providerOrderId)
            && providerOrderId == order.Id;
        bool amountMatches = state.Amount == order.AmountCents;

        if (!paymentMatches || !orderMatches || !amountMatches)
        {
            _logger.LogWarning(
                "Admin resync отклонён для Order {OrderId}: "
                + "snapshot mismatch (payment={PaymentMatches}, order={OrderMatches}, amount={AmountMatches})",
                order.Id,
                paymentMatches,
                orderMatches,
                amountMatches);
            return Error.Validation(
                "order.tbank.snapshot_mismatch",
                "T-Bank вернул данные другого платежа или заказа");
        }

        TBankStatusMapping mapping;
        try
        {
            mapping = TBankStatusMapper.Map(state.Status, state.ErrorCode);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex,
                "Resync: unknown T-Bank Status={Status} for Order {OrderId}",
                state.Status, order.Id);
            return Error.Failure("order.tbank.unknown_status",
                $"T-Bank вернул неизвестный статус: {state.Status}");
        }

        // Промежуточный статус (NEW / AUTHORIZING) — no-op, оставляем PENDING.
        if (string.Equals(mapping.NormalizedStatus, "NOOP", StringComparison.Ordinal))
        {
            await _orderEvents.AddAsync(
                OrderEvent.Record(
                    order.Id,
                    OrderEventType.RECONCILIATION_RECOVERED,
                    $$"""{"tbank_status":"{{state.Status}}","mapped":"NOOP","triggered_by":"admin"}""",
                    actorUserId: _user.UserId),
                cancellationToken);

            UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
            if (save.IsFailure) return save.Error;

            return new ResyncOrderResponse(previousStatus, order.Status.ToString());
        }

        // Применяем результат через тот же idempotent path что и webhook.
        PaymentWebhookRequest request = new(
            OrderId: order.Id,
            ExternalRef: order.ExternalProviderRef,
            Status: mapping.NormalizedStatus,
            Reason: mapping.Reason);

        UnitResult<Error> handleResult = await _webhookHandler.Handle(request, cancellationToken);
        if (handleResult.IsFailure)
        {
            return handleResult.Error;
        }

        await _orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                OrderEventType.RECONCILIATION_RECOVERED,
                $$"""{"tbank_status":"{{state.Status}}","mapped":"{{mapping.NormalizedStatus}}","triggered_by":"admin"}""",
                actorUserId: _user.UserId),
            cancellationToken);

        UnitResult<Error> finalSave = await _transactions.SaveChangesAsync(cancellationToken);
        if (finalSave.IsFailure)
        {
            return finalSave.Error;
        }

        // Re-load для актуального статуса (PaymentWebhookHandler уже сохранил изменения).
        Result<Order, Error> reloaded = await _orders.GetByAsync(
            o => o.Id == order.Id, cancellationToken);
        string newStatus = reloaded.IsSuccess
            ? reloaded.Value.Status.ToString()
            : order.Status.ToString();

        _logger.LogInformation(
            "Admin resync для Order {OrderId}: {Previous} → {New} (T-Bank={TBank}, by={Actor})",
            order.Id, previousStatus, newStatus, state.Status, _user.UserId);

        return new ResyncOrderResponse(previousStatus, newStatus);
    }
}
