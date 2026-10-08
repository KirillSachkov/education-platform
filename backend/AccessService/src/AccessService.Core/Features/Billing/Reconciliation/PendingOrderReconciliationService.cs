using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Core.Domain;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Core.Features.Billing.UseCases;
using AccessService.Domain;
using Core.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AccessService.Core.Features.Billing.Reconciliation;

/// <summary>
/// BackgroundService — каждые <see cref="ReconciliationOptions.IntervalSeconds"/>
/// собирает PENDING <see cref="Order"/>'ы старше <see cref="ReconciliationOptions.MinAgeSecondsBeforePoll"/>,
/// дёргает <see cref="ITBankClient.GetStateAsync"/> и применяет результат через тот же
/// idempotent path что и webhook (<see cref="PaymentWebhookHandler"/>).
///
/// Главная защита от потерянных T-Bank webhook'ов: один потерянный webhook = один
/// потерянный grant = жалоба клиента. Без этого сервиса не выходим в прод.
///
/// Phase F.1.4 — issue #102.
/// </summary>
public sealed class PendingOrderReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ReconciliationOptions> _options;
    private readonly ILogger<PendingOrderReconciliationService> _logger;

    public PendingOrderReconciliationService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ReconciliationOptions> options,
        ILogger<PendingOrderReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PendingOrderReconciliationService started (interval={Interval}s)",
            _options.CurrentValue.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Do not catch general exception types — BG-loop must keep running
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Reconciliation tick failed; will retry");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PendingOrderReconciliationService stopped");
    }

    /// <summary>
    /// Один tick реконсилиации. Public для тестов — вызывается напрямую без BackgroundService.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        ReconciliationOptions opts = _options.CurrentValue;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset minAgeCutoff = now.AddSeconds(-opts.MinAgeSecondsBeforePoll);
        DateTimeOffset retryCutoff = now.AddSeconds(-Math.Max(1, opts.RetryDeferralSeconds));
        DateTimeOffset refundRetryCutoff = now.AddSeconds(-Math.Max(1, opts.RefundPollIntervalSeconds));
        DateTimeOffset refundCreatedAfter = now.AddDays(-Math.Max(1, opts.RefundLookbackDays));
        DateTimeOffset expireCutoff = now.AddHours(-opts.MaxPendingHoursBeforeExpire);
        DateTimeOffset idempotencyCutoff = now.AddHours(-opts.IdempotencyKeyTtlHours);

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        IOrdersRepository orders = sp.GetRequiredService<IOrdersRepository>();
        IOrderEventsRepository orderEvents = sp.GetRequiredService<IOrderEventsRepository>();
        IPlansRepository plans = sp.GetRequiredService<IPlansRepository>();
        ITBankClient tbank = sp.GetRequiredService<ITBankClient>();
        PaymentWebhookHandler webhookHandler = sp.GetRequiredService<PaymentWebhookHandler>();
        ITransactionManager transactions = sp.GetRequiredService<ITransactionManager>();
        PaymentMetrics metrics = sp.GetRequiredService<PaymentMetrics>();
        IIdempotencyKeyRepository idempotencyKeys = sp.GetRequiredService<IIdempotencyKeyRepository>();

        // Опционально: каждый tick подчищаем старые idempotency-keys, чтобы таблица
        // не росла безлимитно. Дешёвая операция (ExecuteDelete с partial index по
        // created_at). Делаем перед основным batch'ом — даже если pending пуст.
        int deleted = await idempotencyKeys.DeleteOlderThanAsync(idempotencyCutoff, ct);
        if (deleted > 0)
        {
            _logger.LogInformation(
                "Reconciliation: cleaned up {Count} idempotency keys older than {Cutoff}",
                deleted, idempotencyCutoff);
        }

        IReadOnlyList<Order> pending = await orders.GetPendingForReconciliationAsync(
            provider: "tbank",
            minAgeCutoff: minAgeCutoff,
            retryCutoff: retryCutoff,
            limit: opts.BatchSize,
            ct);

        IReadOnlyList<Order> paidSubscriptionsWithoutRebill =
            await orders.GetPaidSubscriptionsWithoutRebillAsync(
                provider: "tbank",
                minAgeCutoff: minAgeCutoff,
                retryCutoff: retryCutoff,
                limit: opts.BatchSize,
                ct);

        IReadOnlyList<Order> paidRenewals =
            await orders.GetPaidRenewalsForRefundReconciliationAsync(
                provider: "tbank",
                createdAfter: refundCreatedAfter,
                retryCutoff: refundRetryCutoff,
                limit: opts.BatchSize,
                ct);

        if (pending.Count == 0 && paidSubscriptionsWithoutRebill.Count == 0 && paidRenewals.Count == 0) return;

        foreach (Order renewalOrder in paidRenewals)
        {
            if (ct.IsCancellationRequested) break;

            Result<TBankGetStateResponse, Error> stateResult =
                await tbank.GetStateAsync(renewalOrder.ExternalProviderRef!, ct);
            string outcome = stateResult.IsSuccess
                ? stateResult.Value.Status
                : ErrorCode(stateResult.Error);

            if (stateResult.IsSuccess
                && string.Equals(stateResult.Value.Status, "REFUNDED", StringComparison.Ordinal))
            {
                UnitResult<Error> refunded = await webhookHandler.Handle(
                    new PaymentWebhookRequest(
                        renewalOrder.Id,
                        renewalOrder.ExternalProviderRef!,
                        "REFUNDED",
                        stateResult.Value.Message,
                        RebillId: null),
                    ct);
                if (refunded.IsFailure)
                {
                    _logger.LogError(
                        "Reconciliation: renewal refund failed for Order {OrderId}: {Error}",
                        renewalOrder.Id,
                        refunded.Error.Type);
                    await orderEvents.AddAsync(
                        OrderEvent.Record(
                            renewalOrder.Id,
                            OrderEventType.REFUND_RECONCILIATION_CHECKED,
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                outcome = "refund_apply_failed",
                                error = ErrorCode(refunded.Error),
                            })),
                        ct);
                    await transactions.SaveChangesAsync(ct);
                }

                continue;
            }

            await orderEvents.AddAsync(
                OrderEvent.Record(
                    renewalOrder.Id,
                    OrderEventType.REFUND_RECONCILIATION_CHECKED,
                    System.Text.Json.JsonSerializer.Serialize(new { outcome })),
                ct);
            await transactions.SaveChangesAsync(ct);
        }

        foreach (Order paidOrder in paidSubscriptionsWithoutRebill)
        {
            string? rebillId = await TryRecoverRebillIdAsync(
                paidOrder, tbank, plans, metrics, orderEvents, transactions, ct);
            if (rebillId is null)
            {
                continue;
            }

            UnitResult<Error> result = await webhookHandler.Handle(
                new PaymentWebhookRequest(
                    paidOrder.Id,
                    paidOrder.ExternalProviderRef!,
                    "AUTHORIZED",
                    null,
                    rebillId),
                ct);
            if (result.IsFailure)
            {
                _logger.LogError(
                    "Reconciliation: failed to attach recovered RebillId for paid Order {OrderId}: {Error}",
                    paidOrder.Id,
                    result.Error.Type);
            }
        }

        foreach (Order order in pending)
        {
            if (ct.IsCancellationRequested) break;

            TBankGetStateResponse state;
            if (string.IsNullOrWhiteSpace(order.ExternalProviderRef))
            {
                Result<TBankCheckOrderResponse, Error> checkOrder =
                    await tbank.CheckOrderAsync(order.Id.ToString(), ct);
                if (checkOrder.IsFailure)
                {
                    metrics.RecordCheckOrderRecovery("tbank", ErrorCode(checkOrder.Error));
                    _logger.LogWarning(
                        "Reconciliation: CheckOrder failed for Order {OrderId}: {Reason}",
                        order.Id,
                        ErrorCode(checkOrder.Error));
                    await RecordDeferredAsync(
                        order, ErrorCode(checkOrder.Error), orderEvents, transactions, ct);
                    continue;
                }

                Result<TBankPaymentHistory, Error> recovery =
                    TBankOrderRecovery.MatchAndAttach(order, checkOrder.Value);
                if (recovery.IsFailure)
                {
                    metrics.RecordCheckOrderRecovery("tbank", ErrorCode(recovery.Error));
                    _logger.LogWarning(
                        "Reconciliation: CheckOrder fail-closed for Order {OrderId}: {Reason}",
                        order.Id,
                        ErrorCode(recovery.Error));
                    await RecordDeferredAsync(
                        order, ErrorCode(recovery.Error), orderEvents, transactions, ct);
                    continue;
                }

                UnitResult<Error> checkpoint = await transactions.SaveChangesAsync(ct);
                if (checkpoint.IsFailure)
                {
                    _logger.LogError(
                        "Reconciliation: failed to persist recovered PaymentId for Order {OrderId}: {Error}",
                        order.Id,
                        checkpoint.Error.Type);
                    continue;
                }

                TBankPaymentHistory payment = recovery.Value;
                metrics.RecordCheckOrderRecovery("tbank", "recovered");
                state = new TBankGetStateResponse
                {
                    Success = payment.Success,
                    ErrorCode = payment.ErrorCode?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Message = payment.Message,
                    Status = payment.Status,
                    PaymentId = payment.PaymentId,
                    OrderId = order.Id.ToString(),
                    Amount = payment.Amount,
                };
            }
            else
            {
                // Poll T-Bank before applying the local expiry cutoff. A delayed CONFIRMED
                // webhook must still recover the paid order instead of revoking its audit trail.
                Result<TBankGetStateResponse, Error> stateResult =
                    await tbank.GetStateAsync(order.ExternalProviderRef, ct);
                if (stateResult.IsFailure)
                {
                    _logger.LogDebug(
                        "Reconciliation: GetStateAsync failed for Order {OrderId}: {Error} — will retry next tick",
                        order.Id, stateResult.Error.Type);
                    await RecordDeferredAsync(
                        order, ErrorCode(stateResult.Error), orderEvents, transactions, ct);
                    continue;
                }

                state = stateResult.Value;
            }

            TBankStatusMapping mapping;
            try
            {
                mapping = TBankStatusMapper.Map(state.Status, state.ErrorCode);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Reconciliation: unknown T-Bank Status={Status} for Order {OrderId}",
                    state.Status, order.Id);
                await RecordDeferredAsync(
                    order, "unknown_status", orderEvents, transactions, ct);
                continue;
            }

            if (string.Equals(mapping.NormalizedStatus, "NOOP", StringComparison.Ordinal))
            {
                // #414 — частичный возврат у PENDING-заказа практически невозможен, но если
                // T-Bank вернёт PARTIAL_REFUNDED — пишем тот же audit-row, что и webhook-путь
                // (доступ сохраняется, Order не меняет статус).
                if (string.Equals(state.Status, "PARTIAL_REFUNDED", StringComparison.Ordinal))
                {
                    await orderEvents.AddAsync(
                        OrderEvent.Record(order.Id, OrderEventType.PARTIAL_REFUND_IGNORED,
                            $$"""{"source":"reconciliation","tbank_status":"{{state.Status}}"}"""),
                        ct);
                    UnitResult<Error> partialSave = await transactions.SaveChangesAsync(ct);
                    if (partialSave.IsFailure)
                    {
                        _logger.LogError(
                            "Reconciliation PARTIAL_REFUNDED audit save failed for Order {OrderId}: {Error}",
                            order.Id, partialSave.Error.Type);
                    }
                }

                // Provider confirms that the payment is still non-terminal. Only now is it
                // safe to apply the local hard-expiry cutoff.
                if (order.CreatedAt < expireCutoff)
                {
                    UnitResult<Error> markExpired = order.MarkFailed("reconciliation_expired");
                    if (markExpired.IsFailure)
                    {
                        _logger.LogWarning(
                            "Reconciliation: failed to mark Order {OrderId} as expired: {Error}",
                            order.Id, markExpired.Error.Type);
                        continue;
                    }

                    await orderEvents.AddAsync(
                        OrderEvent.Record(order.Id, OrderEventType.MARK_FAILED,
                            $$"""{"reason":"reconciliation_expired","threshold_hours":{{opts.MaxPendingHoursBeforeExpire}}}"""),
                        ct);
                    metrics.RecordOrderFailed("tbank", "reconciliation_expired");
                    await MarkReservationRecoveredAsync(order.Id, idempotencyKeys, ct);

                    UnitResult<Error> save = await transactions.SaveChangesAsync(ct);
                    if (save.IsFailure)
                    {
                        _logger.LogError(
                            "Reconciliation save failed for expired Order {OrderId}: {Error}",
                            order.Id, save.Error.Type);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Order {OrderId} expired after {Hours}h pending — marked FAILED",
                            order.Id, opts.MaxPendingHoursBeforeExpire);
                    }
                }
                else
                {
                    await RecordDeferredAsync(
                        order, $"provider_state:{state.Status}", orderEvents, transactions, ct);
                }

                continue;
            }

            string? recoveredRebillId = null;
            if (mapping.NormalizedStatus is "AUTHORIZED" or "PAID"
                && order.RebillId is null)
            {
                recoveredRebillId = await TryRecoverRebillIdAsync(
                    order, tbank, plans, metrics, orderEvents, transactions, ct);
            }

            // Build the same PaymentWebhookRequest the webhook adapter would have built.
            PaymentWebhookRequest request = new(
                OrderId: order.Id,
                ExternalRef: order.ExternalProviderRef!,
                Status: mapping.NormalizedStatus,
                Reason: mapping.Reason,
                RebillId: recoveredRebillId);

            UnitResult<Error> handleResult = await webhookHandler.Handle(request, ct);
            if (handleResult.IsFailure)
            {
                _logger.LogError(
                    "Reconciliation: PaymentWebhookHandler failed for Order {OrderId}: {Error}",
                    order.Id, handleResult.Error.Type);
                continue;
            }

            bool isTerminal = mapping.NormalizedStatus is "PAID" or "FAILED" or "REFUNDED";
            if (isTerminal)
            {
                await orderEvents.AddAsync(
                    OrderEvent.Record(order.Id, OrderEventType.RECONCILIATION_RECOVERED,
                        $$"""{"tbank_status":"{{state.Status}}","mapped":"{{mapping.NormalizedStatus}}"}"""),
                    ct);
                metrics.RecordReconciliationRecovered("tbank", state.Status);
                await MarkReservationRecoveredAsync(order.Id, idempotencyKeys, ct);
            }

            UnitResult<Error> finalSave = await transactions.SaveChangesAsync(ct);
            if (finalSave.IsFailure)
            {
                _logger.LogError("Reconciliation final save failed for Order {OrderId}: {Error}",
                    order.Id, finalSave.Error.Type);
            }
            else
            {
                _logger.LogInformation(
                    "Reconciliation recovered Order {OrderId}: T-Bank={TBankStatus} → {Mapped}",
                    order.Id, state.Status, mapping.NormalizedStatus);
            }
        }
    }

    private async Task<string?> TryRecoverRebillIdAsync(
        Order order,
        ITBankClient tbank,
        IPlansRepository plans,
        PaymentMetrics metrics,
        IOrderEventsRepository orderEvents,
        ITransactionManager transactions,
        CancellationToken ct)
    {
        Result<Plan, Error> planResult = await plans.GetByAsync(p => p.Id == order.PlanId, ct);
        if (planResult.IsFailure || planResult.Value.Tier != PlanTier.SUBSCRIPTION)
        {
            return null;
        }

        Result<IReadOnlyList<TBankCard>, Error> cardsResult =
            await tbank.GetCardListAsync(order.UserId.ToString(), ct);
        if (cardsResult.IsFailure)
        {
            metrics.RecordRebillRecovery("tbank", "provider_error");
            _logger.LogWarning(
                "Reconciliation: GetCardList failed for subscription Order {OrderId}: {Reason}",
                order.Id,
                ErrorCode(cardsResult.Error));
            await RecordDeferredAsync(
                order, ErrorCode(cardsResult.Error), orderEvents, transactions, ct);
            return null;
        }

        TBankCard[] candidates = cardsResult.Value
            .Where(card => string.Equals(card.Status, "A", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(card.RebillId))
            .ToArray();
        if (candidates.Length != 1)
        {
            string outcome = candidates.Length == 0 ? "none" : "ambiguous";
            metrics.RecordRebillRecovery("tbank", outcome);
            _logger.LogWarning(
                "Reconciliation: RebillId recovery fail-closed for Order {OrderId}: {Outcome} ({CandidateCount} candidates)",
                order.Id,
                outcome,
                candidates.Length);
            await RecordDeferredAsync(
                order, $"rebill:{outcome}", orderEvents, transactions, ct);
            return null;
        }

        metrics.RecordRebillRecovery("tbank", "recovered");
        return candidates[0].RebillId;
    }

    private async Task RecordDeferredAsync(
        Order order,
        string reason,
        IOrderEventsRepository orderEvents,
        ITransactionManager transactions,
        CancellationToken ct)
    {
        await orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                OrderEventType.RECONCILIATION_DEFERRED,
                System.Text.Json.JsonSerializer.Serialize(new { reason })),
            ct);
        UnitResult<Error> save = await transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            _logger.LogError(
                "Reconciliation: failed to persist deferral for Order {OrderId}: {Error}",
                order.Id,
                save.Error.Type);
        }
    }

    private static async Task MarkReservationRecoveredAsync(
        Guid orderId,
        IIdempotencyKeyRepository idempotencyKeys,
        CancellationToken ct)
    {
        IdempotencyKey? reservation = await idempotencyKeys.GetByOrderIdAsync(orderId, ct);
        if (reservation?.Status == IdempotencyKeyStatus.PROCESSING)
        {
            reservation.RecoverTerminal();
        }
    }

    private static string ErrorCode(Error error) =>
        error.Messages.Count > 0 ? error.Messages[0].Code : "unknown";
}
