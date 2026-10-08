using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Core.Features.Billing.UseCases;
using AccessService.Domain;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.Web.Jobs;

/// <summary>
/// Periodically auto-renews recurring (subscription) <see cref="PlanGrant"/>s whose
/// <c>NextChargeAt</c> has come due (#614). For each due grant it server-side charges the
/// saved card via the provider <c>RebillId</c> (no redirect), then on success extends the
/// grant by the plan's recurring interval and publishes <see cref="PlanGrantRenewed"/>; on
/// failure it records the failure (incrementing the retry counter + scheduling the next policy
/// retry) and, once <c>ChargeFailureCount</c> reaches the policy limit, publishes the
/// terminal <see cref="PlanGrantRenewalFailed"/> and lets the grant run out naturally
/// (<see cref="ExpiredGrantsSweeper"/> expires it — we do NOT force-revoke).
///
/// Per-grant flow: acquire a cross-replica advisory lock → resume or create a durable PENDING
/// RENEWAL <see cref="Order"/> → provider Init → persist PaymentId → Charge. CONFIRMED is
/// applied through <c>PaymentWebhookHandler</c>, which atomically marks the order PAID,
/// renews the grant and publishes the outbox event. A crash after Charge is recovered by
/// GetState/webhook without issuing another payment.
///
/// Idempotency: after a successful Renew, <c>NextChargeAt</c> moves forward by one interval
/// so the grant isn't re-picked; after a failure it gets a future retry <c>NextChargeAt</c>.
/// Period: <c>SweepInterval</c> (default 1h). Hosted only in non-Testing environments;
/// integration tests instantiate it directly and invoke <see cref="SweepOnceAsync"/>.
/// </summary>
public sealed class RecurringChargesSweeper : BackgroundService
{
    private const string Provider = "tbank";

    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<RecurringChargesSweeperOptions> _options;
    private readonly ILogger<RecurringChargesSweeper> _logger;

    public RecurringChargesSweeper(
        IServiceProvider services,
        IOptionsMonitor<RecurringChargesSweeperOptions> options,
        ILogger<RecurringChargesSweeper> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay so the host can come up fully before we touch the DB / provider.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RecurringChargesSweeper iteration failed; will retry next tick");
            }

            try
            {
                await Task.Delay(_options.CurrentValue.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Returns the number of grants successfully renewed in this tick.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        RecurringChargesSweeperOptions opts = _options.CurrentValue;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int batchSize = Math.Max(1, opts.BatchSize);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IPlanGrantsRepository grants = scope.ServiceProvider.GetRequiredService<IPlanGrantsRepository>();
        // Due recurring grants: ACTIVE, has saved RebillId, NextChargeAt elapsed, and the
        // retry budget is not yet exhausted. Bounded LIMIT in SQL avoids loading an unbounded
        // set on a renewal storm. The plan's Term.Kind==RECURRING gate is applied after the
        // plan batch-fetch (Term is an owned VO, not a flat PlanGrant column).
        IReadOnlyList<PlanGrant> batch = await grants.GetDueRecurringBatchAsync(
            now,
            batchSize,
            cancellationToken);

        if (batch.Count == 0)
        {
            return 0;
        }

        IAuthServiceClient auth = scope.ServiceProvider.GetRequiredService<IAuthServiceClient>();
        Guid[] userIds = batch.Select(g => g.UserId).Distinct().ToArray();
        Result<IReadOnlyList<AuthUserLookupDto>, Error> users =
            await auth.GetUsersByIdsAsync(userIds, cancellationToken);
        Dictionary<Guid, string> emailsByUserId = users.IsSuccess
            ? users.Value.ToDictionary(u => u.UserId, u => u.Email)
            : [];

        int renewed = 0;
        foreach (PlanGrant grant in batch)
        {
            // Re-open each grant in its own scope after acquiring a cross-replica lock.
            // Both grant and plan may be stale by the time another worker finishes.
            bool didRenew = await ChargeOneInScopeAsync(
                grant.Id,
                grant.PlanId,
                now,
                emailsByUserId.GetValueOrDefault(grant.UserId),
                cancellationToken);
            if (didRenew)
            {
                renewed++;
            }
        }

        if (renewed > 0)
        {
            _logger.LogInformation(
                "RecurringChargesSweeper renewed {Renewed}/{Total} due subscription grant(s)",
                renewed, batch.Count);
        }

        return renewed;
    }

    private async Task<bool> ChargeOneInScopeAsync(
        Guid grantId,
        Guid planId,
        DateTimeOffset now,
        string? userEmail,
        CancellationToken ct)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        IOrdersRepository orders = services.GetRequiredService<IOrdersRepository>();

        IAsyncDisposable? renewalLock = await orders.TryAcquireRenewalLockAsync(grantId, ct);
        if (renewalLock is null)
        {
            _logger.LogDebug(
                "RecurringChargesSweeper: grant {GrantId} is being processed by another replica",
                grantId);
            return false;
        }

        try
        {
            IPlanGrantsRepository grants = services.GetRequiredService<IPlanGrantsRepository>();
            IPlansRepository plans = services.GetRequiredService<IPlansRepository>();
            Result<PlanGrant, Error> grantResult = await grants.GetByAsync(g => g.Id == grantId, ct);
            if (grantResult.IsFailure)
            {
                return false;
            }

            Result<Plan, Error> planResult = await plans.GetByAsync(p => p.Id == planId, ct);
            if (planResult.IsFailure)
            {
                _logger.LogWarning(
                    "Plan {PlanId} not found while renewing grant {GrantId}; skipping",
                    planId,
                    grantId);
                return false;
            }

            Plan plan = planResult.Value;
            if (!plan.IsActive
                || plan.ArchivedAt is not null
                || plan.Term.Kind != PlanTermKind.RECURRING
                || plan.Term.RecurringIntervalDays is not > 0
                || plan.EffectivePriceCents(now) is not > 0)
            {
                return false;
            }

            PlanGrant grant = grantResult.Value;
            if (grant.Status != PlanGrantStatus.ACTIVE
                || string.IsNullOrWhiteSpace(grant.RebillId)
                || grant.NextChargeAt is null
                || grant.NextChargeAt > now
                || grant.AutoRenewalCancelledAt is not null
                || grant.AccessEndsAt <= now
                || grant.ChargeFailureCount >= SubscriptionRenewalPolicy.MAX_ATTEMPTS)
            {
                return false;
            }

            ITBankClient tbank = services.GetRequiredService<ITBankClient>();
            TBankReceiptBuilder receiptBuilder = services.GetRequiredService<TBankReceiptBuilder>();
            RenewalFailureRecorder renewalFailures =
                services.GetRequiredService<RenewalFailureRecorder>();
            IOrderEventsRepository orderEvents = services.GetRequiredService<IOrderEventsRepository>();
            ITransactionManager transactions = services.GetRequiredService<ITransactionManager>();
            PaymentWebhookHandler webhookHandler = services.GetRequiredService<PaymentWebhookHandler>();
            return await ChargeOneAsync(
                grant,
                plan,
                now,
                orders,
                tbank,
                receiptBuilder,
                webhookHandler,
                orderEvents,
                renewalFailures,
                transactions,
                userEmail,
                ct);
        }
        finally
        {
            await renewalLock.DisposeAsync();
        }
    }

    private async Task<bool> ChargeOneAsync(
        PlanGrant grant,
        Plan plan,
        DateTimeOffset now,
        IOrdersRepository orders,
        ITBankClient tbank,
        TBankReceiptBuilder receiptBuilder,
        PaymentWebhookHandler webhookHandler,
        IOrderEventsRepository orderEvents,
        RenewalFailureRecorder renewalFailures,
        ITransactionManager transactions,
        string? userEmail,
        CancellationToken ct)
    {
        long amountCents = plan.EffectivePriceCents(now) ?? plan.PriceCents ?? 0;
        if (amountCents <= 0)
        {
            _logger.LogWarning(
                "RecurringChargesSweeper: plan {PlanId} has no positive price; cannot renew grant {GrantId}",
                plan.Id, grant.Id);
            return false;
        }

        // Resume a durable attempt after a process crash. The advisory lock prevents two
        // replicas from creating/charging attempts for the same grant concurrently.
        Result<Order, Error> pendingOrder = await orders.GetByAsync(
            o => o.UserId == grant.UserId
              && o.PlanId == plan.Id
              && o.ChargeType == OrderChargeType.RENEWAL
              && o.RebillId == grant.RebillId
              && o.Status == OrderStatus.PENDING,
            ct);

        Order order;
        bool resumedAttempt = pendingOrder.IsSuccess;
        if (resumedAttempt)
        {
            order = pendingOrder.Value;
        }
        else
        {
            Result<Order, Error> createOrder = Order.CreateRenewal(
                grant.UserId,
                plan.Id,
                grant.Id,
                amountCents,
                plan.Currency,
                grant.RebillId!,
                provider: Provider);
            if (createOrder.IsFailure)
            {
                _logger.LogError(
                    "RecurringChargesSweeper: failed to build renewal order for grant {GrantId}: {Error}",
                    grant.Id, createOrder.Error.Type);
                return false;
            }

            order = createOrder.Value;
            await orders.AddAsync(order, ct);
            UnitResult<Error> saveOrder = await transactions.SaveChangesAsync(ct);
            if (saveOrder.IsFailure)
            {
                _logger.LogError(
                    "RecurringChargesSweeper: failed to persist pending order for grant {GrantId}: {Error}",
                    grant.Id, saveOrder.Error.Type);
                return false;
            }
        }

        TBankPaymentHistory? recoveredPayment = null;

        // 2. A resumed durable attempt without PaymentId is the ambiguous crash window:
        //    provider Init may have succeeded before the process died. Recover by merchant
        //    OrderId before considering any new Init; a missing/ambiguous history fails closed.
        if (string.IsNullOrWhiteSpace(order.ExternalProviderRef))
        {
            if (resumedAttempt)
            {
                Result<TBankCheckOrderResponse, Error> checkOrder =
                    await tbank.CheckOrderAsync(order.Id.ToString(), ct);
                if (checkOrder.IsFailure)
                {
                    _logger.LogWarning(
                        "RecurringChargesSweeper: CheckOrder failed for resumed order {OrderId}: {Reason}",
                        order.Id,
                        ErrorCode(checkOrder.Error));
                    return false;
                }

                Result<TBankPaymentHistory, Error> recovery =
                    TBankOrderRecovery.MatchAndAttach(order, checkOrder.Value);
                if (recovery.IsFailure)
                {
                    _logger.LogWarning(
                        "RecurringChargesSweeper: CheckOrder fail-closed for resumed order {OrderId}: {Reason}",
                        order.Id,
                        ErrorCode(recovery.Error));
                    return false;
                }

                recoveredPayment = recovery.Value;
            }
            else
            {
                string paymentDescription = $"Продление подписки «{plan.DisplayName.Value}»";
                if (paymentDescription.Length > TBankInitRequest.DESCRIPTION_MAX_LENGTH)
                {
                    paymentDescription = paymentDescription[..TBankInitRequest.DESCRIPTION_MAX_LENGTH];
                }

                TBankInitRequest initRequest = new()
                {
                    Amount = order.AmountCents,
                    OrderId = order.Id.ToString(),
                    Description = paymentDescription,
                    PayType = "O",
                    CustomerKey = grant.CustomerKey ?? grant.UserId.ToString(),
                    DATA = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["OperationInitiatorType"] = "R",
                    },
                    Receipt = receiptBuilder.Build(plan, userEmail, order.AmountCents),
                };

                Result<TBankInitResponse, Error> initResult = await tbank.InitAsync(initRequest, ct);
                if (initResult.IsFailure || string.IsNullOrEmpty(initResult.Value.PaymentId))
                {
                    string reason = initResult.IsFailure
                        ? ErrorCode(initResult.Error)
                        : "init.missing_payment_id";

                    if (initResult.IsFailure
                        && string.Equals(reason, "tbank.network.error", StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "RecurringChargesSweeper: Init response was ambiguous for order {OrderId}; leaving PENDING for CheckOrder",
                            order.Id);
                        return false;
                    }

                    return await RecordFailureAsync(
                        grant, plan, order, $"init: {reason}", now, renewalFailures, transactions, ct);
                }

                UnitResult<Error> attach = order.AttachExternalRef(initResult.Value.PaymentId);
                if (attach.IsFailure)
                {
                    return await RecordFailureAsync(
                        grant, plan, order, $"attach: {attach.Error.Type}", now, renewalFailures, transactions, ct);
                }
            }

            // Persist PaymentId before Charge or applying provider state. A crash after this
            // point can be resumed by GetState/webhook without another Init.
            UnitResult<Error> savePaymentId = await transactions.SaveChangesAsync(ct);
            if (savePaymentId.IsFailure)
            {
                _logger.LogError(
                    "RecurringChargesSweeper: failed to persist PaymentId for order {OrderId}: {Error}",
                    order.Id, savePaymentId.Error.Type);
                return false;
            }
        }

        string paymentId = order.ExternalProviderRef!;

        // Existing PaymentId means this is a resumed attempt. Query provider state first;
        // repeat Charge only for explicit NEW, the safe pre-charge state.
        if (resumedAttempt)
        {
            string providerStatus;
            if (recoveredPayment is not null)
            {
                providerStatus = recoveredPayment.Status.ToUpperInvariant();
            }
            else
            {
                Result<TBankGetStateResponse, Error> stateResult = await tbank.GetStateAsync(paymentId, ct);
                if (stateResult.IsFailure)
                {
                    _logger.LogWarning(
                        "RecurringChargesSweeper: GetState failed for pending order {OrderId}; leaving it pending",
                        order.Id);
                    return false;
                }

                providerStatus = stateResult.Value.Status.ToUpperInvariant();
            }

            if (string.Equals(providerStatus, "CONFIRMED", StringComparison.Ordinal))
            {
                return await ApplyConfirmedRenewalAsync(
                    order, grant, paymentId, webhookHandler, transactions, ct);
            }

            if (!string.Equals(providerStatus, "NEW", StringComparison.Ordinal))
            {
                if (providerStatus is "REJECTED" or "REVERSED" or "DEADLINE_EXPIRED"
                    or "ATTEMPTS_EXPIRED" or "CANCELED" or "REFUNDED")
                {
                    return await RecordFailureAsync(
                        grant,
                        plan,
                        order,
                        $"state: {providerStatus}",
                        now,
                        renewalFailures,
                        transactions,
                        ct);
                }

                _logger.LogInformation(
                    "RecurringChargesSweeper: pending order {OrderId} is in provider state {Status}; Charge is not safe to repeat",
                    order.Id,
                    providerStatus);
                return false;
            }

            if (await orderEvents.ExistsAsync(order.Id, OrderEventType.CHARGE_CALLED, ct))
            {
                _logger.LogWarning(
                    "RecurringChargesSweeper: order {OrderId} is still NEW after an ambiguous Charge; refusing a second Charge",
                    order.Id);
                return false;
            }
        }

        await orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                OrderEventType.CHARGE_CALLED,
                $$"""{"provider":"tbank","payment_id":"{{paymentId}}"}"""),
            ct);
        UnitResult<Error> chargeCheckpoint = await transactions.SaveChangesAsync(ct);
        if (chargeCheckpoint.IsFailure)
        {
            _logger.LogError(
                "RecurringChargesSweeper: failed to persist Charge checkpoint for order {OrderId}: {Error}",
                order.Id,
                chargeCheckpoint.Error.Type);
            return false;
        }

        // Server-side charge of the saved card.
        Result<TBankChargeResponse, Error> chargeResult = await tbank.ChargeAsync(
            paymentId, grant.RebillId!, ct);

        if (chargeResult.IsFailure)
        {
            _logger.LogWarning(
                "RecurringChargesSweeper: Charge failed for order {OrderId} ({Reason}); leaving pending for authoritative GetState",
                order.Id,
                ErrorCode(chargeResult.Error));
            return false;
        }

        if (!string.Equals(chargeResult.Value.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "RecurringChargesSweeper: Charge for order {OrderId} returned {Status}; leaving pending for GetState",
                order.Id,
                chargeResult.Value.Status);
            return false;
        }

        return await ApplyConfirmedRenewalAsync(
            order, grant, paymentId, webhookHandler, transactions, ct);
    }

    private async Task<bool> ApplyConfirmedRenewalAsync(
        Order order,
        PlanGrant grant,
        string paymentId,
        PaymentWebhookHandler webhookHandler,
        ITransactionManager transactions,
        CancellationToken ct)
    {
        UnitResult<Error> result = await webhookHandler.Handle(
            new PaymentWebhookRequest(
                order.Id,
                paymentId,
                "PAID",
                null,
                grant.RebillId),
            ct);

        if (result.IsFailure)
        {
            _logger.LogError(
                "RecurringChargesSweeper: failed to apply confirmed renewal for order {OrderId}: {Error}",
                order.Id,
                result.Error.Type);
            return false;
        }

        UnitResult<Error> save = await transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            _logger.LogError(
                "RecurringChargesSweeper: failed to persist confirmed renewal for order {OrderId}: {Error}",
                order.Id,
                save.Error.Type);
            return false;
        }

        _logger.LogInformation(
            "RecurringChargesSweeper: confirmed renewal for grant {GrantId} (order {OrderId})",
            grant.Id,
            order.Id);
        return true;
    }

    /// <summary>
    /// Records a failed renewal attempt: marks the order FAILED, bumps the grant's failure
    /// counter and schedules the next policy retry. When the counter reaches the policy limit,
    /// publishes the terminal <see cref="PlanGrantRenewalFailed"/> and does NOT renew — the grant
    /// remains active through the canonical hard grace boundary and is expired naturally by
    /// <see cref="ExpiredGrantsSweeper"/>. Saves atomically. Returns <c>false</c> (no renewal).
    /// </summary>
    private async Task<bool> RecordFailureAsync(
        PlanGrant grant,
        Plan plan,
        Order order,
        string reason,
        DateTimeOffset now,
        RenewalFailureRecorder renewalFailures,
        ITransactionManager transactions,
        CancellationToken ct)
    {
        Result<RenewalFailureTransition, Error> recorded = await renewalFailures.RecordAsync(
            grant,
            plan,
            order,
            reason,
            now);
        if (recorded.IsFailure)
        {
            _logger.LogError(
                "RecurringChargesSweeper: RecordChargeFailure failed for grant {GrantId}: {Error}",
                grant.Id, recorded.Error.Type);
            return false;
        }

        RenewalFailureTransition transition = recorded.Value;

        if (transition.IsTerminal)
        {
            _logger.LogWarning(
                "RecurringChargesSweeper: grant {GrantId} reached {Count} consecutive renewal failures — terminal dunning ({Reason})",
                grant.Id, grant.ChargeFailureCount, reason);
        }
        else
        {
            _logger.LogWarning(
                "RecurringChargesSweeper: renewal failed for grant {GrantId} (attempt {Count}/{Max}, retry at {NextRetryAt:O}): {Reason}",
                grant.Id,
                grant.ChargeFailureCount,
                SubscriptionRenewalPolicy.MAX_ATTEMPTS,
                transition.NextRetryAt,
                reason);
        }

        UnitResult<Error> save = await transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            _logger.LogError(
                "RecurringChargesSweeper: failed to persist renewal failure for grant {GrantId}: {Error}",
                grant.Id, save.Error.Type);
        }

        return false;
    }

    /// <summary>Stable short code of an <see cref="Error"/> for failure-reason strings / logs.</summary>
    private static string ErrorCode(Error error) =>
        error.Messages.Count > 0 ? error.Messages[0].Code : "unknown";
}

public sealed class RecurringChargesSweeperOptions
{
    public const string SectionName = "RecurringChargesSweeper";

    /// <summary>How often the sweeper scans for due recurring grants. Default 1h.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Max grants processed per tick (bounded SQL LIMIT). Default 200.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>Master switch for the hosted sweeper. Default true.</summary>
    public bool Enabled { get; set; } = true;
}
