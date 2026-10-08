using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Core.Features.Billing.Configuration;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Domain;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>
/// T-Bank-specific webhook adapter (Phase F.1.2.1, issue #102). Принимает
/// <see cref="TBankNotification"/> JSON, валидирует sha256 token, маппит
/// T-Bank Status в provider-agnostic <see cref="PaymentWebhookRequest"/>
/// и вызывает existing <see cref="PaymentWebhookHandler"/> (#85) для бизнес-логики.
///
/// Endpoint anonymous — защита через signature only (T-Bank не аутентифицируется
/// через OIDC). Token invalid → 401 + лог + метрика, БЕЗ mutations.
///
/// 200 OK возвращается ТОЛЬКО после COMMIT (T-Bank ретраит на любой не-200).
/// </summary>
public sealed class TBankWebhookEndpoint : IEndpoint
{
    public const long MAX_BODY_BYTES = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/webhooks/tbank/", async (
                HttpContext httpContext,
                [Microsoft.AspNetCore.Mvc.FromServices] TBankWebhookHandler handler,
                CancellationToken ct) =>
            {
                httpContext.Request.EnableBuffering();
                using StreamReader reader = new(httpContext.Request.Body, leaveOpen: true);
                string rawBody = await reader.ReadToEndAsync(ct);
                httpContext.Request.Body.Position = 0;

                TBankNotification? notification;
                try
                {
                    notification = JsonSerializer.Deserialize<TBankNotification>(rawBody, JsonOpts);
                }
                catch (JsonException)
                {
                    return Results.BadRequest("invalid JSON");
                }

                if (notification is null)
                    return Results.BadRequest("empty body");

                UnitResult<Error> result = await handler.Handle(notification, rawBody, ct);

                if (result.IsFailure && result.Error.Messages.Count > 0)
                {
                    string errorCode = result.Error.Messages[0].Code;
                    if (string.Equals(errorCode, "tbank.webhook.invalid_token", StringComparison.Ordinal)
                        || string.Equals(errorCode, "tbank.webhook.wrong_terminal", StringComparison.Ordinal))
                    {
                        return Results.Unauthorized();
                    }
                    if (string.Equals(errorCode, "tbank.webhook.bad_order_id", StringComparison.Ordinal)
                        || string.Equals(errorCode, "tbank.webhook.order_not_found", StringComparison.Ordinal)
                        || string.Equals(errorCode, "tbank.webhook.replay", StringComparison.Ordinal))
                    {
                        return Results.Text("OK", "text/plain");
                    }

                    // Любой прочий failure, включая Begin/CommitTransaction, DB и
                    // concurrency errors, обязан вернуть non-2xx: иначе T-Bank прекратит
                    // доставку до того, как Order/Grant/RebillId/outbox стали durable.
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }

                // Sanity-rejects (amount_mismatch, replay) уже зафиксировали MarkFailed
                // или пометили audit row — return 200, чтобы T-Bank не ретраил их повторно.
                return Results.Text("OK", "text/plain");
            })
            .AllowAnonymous()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MAX_BODY_BYTES))
            .RequireRateLimiting("tbank-webhook");
    }
}

public sealed class TBankWebhookHandler
{
    private readonly IOrdersRepository _orders;
    private readonly IOrderEventsRepository _orderEvents;
    private readonly IPlanGrantsRepository _grants;
    private readonly PaymentWebhookHandler _genericHandler;
    private readonly TBankOptions _options;
    private readonly ITransactionManager _transactions;
    private readonly PaymentMetrics _metrics;
    private readonly ILogger<TBankWebhookHandler> _logger;

    public TBankWebhookHandler(
        IOrdersRepository orders,
        IOrderEventsRepository orderEvents,
        IPlanGrantsRepository grants,
        PaymentWebhookHandler genericHandler,
        IOptions<TBankOptions> options,
        ITransactionManager transactions,
        PaymentMetrics metrics,
        ILogger<TBankWebhookHandler> logger)
    {
        _orders = orders;
        _orderEvents = orderEvents;
        _grants = grants;
        _genericHandler = genericHandler;
        _options = options.Value;
        _transactions = transactions;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(
        TBankNotification notification,
        string rawBody,
        CancellationToken ct)
    {
        string? correlationId = BillingCorrelation.CurrentTraceId();

        // Feature flag: если T-Bank не настроен — webhook отбрасывается. Не должно
        // случаться (T-Bank не достучится до webhook без валидных credentials у
        // нашего terminal'а), но defense-in-depth: если кто-то постучится с
        // подделанным token'ом и пустым _options.Password — token verify завернёт.
        // Тут просто не пускаем сообщение в любую DB-логику.
        if (!_options.IsConfigured)
        {
            _metrics.RecordWebhookReceived("tbank", "not_configured");
            _logger.LogWarning("T-Bank webhook ignored: billing disabled in this environment");
            return Error.Failure("tbank.webhook.not_configured", "Billing disabled");
        }

        // Pre-flight: signature + ownership + format. Mutations не делаем — на любую
        // ошибку валидации просто return без транзакции (read-only заклинание над БД).
        if (!VerifyToken(notification, rawBody))
        {
            _metrics.RecordWebhookReceived("tbank", "invalid_token");
            _logger.LogWarning(
                "T-Bank webhook: invalid token for OrderId={OrderId}",
                notification.OrderId);
            return Error.Failure("tbank.webhook.invalid_token", "Token mismatch");
        }

        if (!string.Equals(notification.TerminalKey, _options.TerminalKey, StringComparison.Ordinal))
        {
            _metrics.RecordWebhookReceived("tbank", "wrong_terminal");
            // Truncate attacker-controlled value: defends against log-injection
            // через гигантские/многострочные TerminalKey'ы.
            string safeTerminalKey = TruncateForLog(notification.TerminalKey, 50);
            _logger.LogWarning(
                "T-Bank webhook: TerminalKey mismatch (got={Got})",
                safeTerminalKey);
            return Error.Failure("tbank.webhook.wrong_terminal", "TerminalKey mismatch");
        }

        if (!Guid.TryParse(notification.OrderId, out Guid orderId))
        {
            _metrics.RecordWebhookReceived("tbank", "bad_order_id");
            string safeOrderId = TruncateForLog(notification.OrderId, 80);
            _logger.LogWarning(
                "T-Bank webhook: OrderId is not a Guid: {OrderId}",
                safeOrderId);
            return Error.Failure("tbank.webhook.bad_order_id", "OrderId не Guid");
        }

        Result<Order, Error> orderResult = await _orders.GetByAsync(o => o.Id == orderId, ct);
        if (orderResult.IsFailure)
        {
            _metrics.RecordWebhookReceived("tbank", "order_not_found");
            _logger.LogWarning(
                "T-Bank webhook: Order {OrderId} not found",
                orderId);
            return Error.Failure("tbank.webhook.order_not_found", "Order не найден");
        }
        Order order = orderResult.Value;

        // Single transaction: all mutations (audit + sub-handler grant write + outbox)
        // commit'ятся атомарно. Sub-handler внутри вызывает _transactions.SaveChangesAsync —
        // с активной transaction TransactionManager выполнит SaveChanges без commit'а.
        UnitResult<Error> beginResult = await _transactions.BeginTransactionAsync(ct);
        if (beginResult.IsFailure) return beginResult;

        // Audit: WEBHOOK_RECEIVED — пишем СРАЗУ после успешной валидации token + locate
        // order. Дальнейшие rejects идут отдельными OrderEvent rows.
        await _orderEvents.AddAsync(
            OrderEvent.Record(order.Id, OrderEventType.WEBHOOK_RECEIVED, ScrubSensitiveFields(rawBody),
                correlationId: correlationId),
            ct);

        // Sanity 1: amount mismatch — НЕ обрабатываем как PAID, маркируем Order FAILED
        // (если ещё PENDING) и возвращаем UnitResult.Success — endpoint вернёт 200 OK,
        // T-Bank не будет ретраить. Audit row WEBHOOK_REJECTED_AMOUNT_MISMATCH.
        if (notification.Amount != order.AmountCents)
        {
            _metrics.RecordWebhookReceived("tbank", "amount_mismatch");
            _logger.LogWarning(
                "T-Bank webhook: amount mismatch — webhook={WebhookAmount} order={OrderAmount} for OrderId={OrderId}",
                notification.Amount, order.AmountCents, order.Id);
            await _orderEvents.AddAsync(
                OrderEvent.Record(order.Id, OrderEventType.WEBHOOK_REJECTED_AMOUNT_MISMATCH, ScrubSensitiveFields(rawBody),
                    correlationId: correlationId),
                ct);

            if (order.Status == OrderStatus.PENDING)
            {
                UnitResult<Error> markFailed = order.MarkFailed(
                    $"amount_mismatch: webhook={notification.Amount}, order={order.AmountCents}");
                if (markFailed.IsFailure)
                {
                    _logger.LogError(
                        "T-Bank webhook: MarkFailed на amount_mismatch вернул {Error}",
                        markFailed.Error.Type);
                }
            }
            UnitResult<Error> commit = await _transactions.CommitTransactionAsync(ct);
            return commit.IsFailure ? commit.Error : UnitResult.Success<Error>();
        }

        // Sanity 2: PaymentId mismatch — Order уже привязан к другому provider ref'у
        // (replay attack или коллизия). Audit + reject, но НЕ меняем Order status.
        string paymentIdStr = notification.PaymentId.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(order.ExternalProviderRef)
            && !string.Equals(order.ExternalProviderRef, paymentIdStr, StringComparison.Ordinal))
        {
            _metrics.RecordWebhookReceived("tbank", "payment_id_mismatch");
            _logger.LogWarning(
                "T-Bank webhook: PaymentId mismatch — webhook={WebhookPid} order.ExternalRef={OrderRef} for OrderId={OrderId}",
                paymentIdStr, order.ExternalProviderRef, order.Id);
            await _orderEvents.AddAsync(
                OrderEvent.Record(order.Id, OrderEventType.WEBHOOK_REJECTED_REPLAY, ScrubSensitiveFields(rawBody),
                    correlationId: correlationId),
                ct);
            UnitResult<Error> commit = await _transactions.CommitTransactionAsync(ct);
            return commit.IsFailure
                ? commit.Error
                : Error.Failure("tbank.webhook.replay", "PaymentId mismatch");
        }

        // Map T-Bank status → нормализованный.
        TBankStatusMapping mapping;
        try
        {
            mapping = TBankStatusMapper.Map(notification.Status, notification.ErrorCode);
        }
        catch (ArgumentException ex)
        {
            _metrics.RecordWebhookReceived("tbank", "unknown_status");
            _logger.LogError(
                ex,
                "T-Bank webhook: unknown Status={Status} for OrderId={OrderId} (требуется обновление кода)",
                notification.Status, order.Id);
            // 200 OK + audit — T-Bank не ретраит. Когда добавим новый Status в маппер,
            // reconciliation подхватит этот заказ.
            UnitResult<Error> commit = await _transactions.CommitTransactionAsync(ct);
            return commit.IsFailure ? commit.Error : UnitResult.Success<Error>();
        }

        // NoOp статусы (NEW, AUTHORIZING, etc.) — пишем audit и возвращаем 200 OK без
        // вызова generic handler'а. T-Bank пришлёт CONFIRMED/REJECTED отдельным hit'ом.
        if (string.Equals(mapping.NormalizedStatus, "NOOP", StringComparison.Ordinal))
        {
            // PARTIAL_REFUNDED — частичные возвраты не поддерживаются (доступ сохраняется,
            // Order не меняет статус). Пишем явный audit-row, чтобы саппорт видел факт при
            // расследовании. Остальные NOOP (NEW/AUTHORIZED/...) — промежуточные, audit не нужен.
            if (string.Equals(notification.Status, "PARTIAL_REFUNDED", StringComparison.Ordinal))
            {
                _metrics.RecordWebhookReceived("tbank", "partial_refund_ignored");
                _logger.LogWarning(
                    "T-Bank webhook: PARTIAL_REFUNDED for Order={OrderId} — частичные возвраты не поддерживаются, доступ сохранён",
                    order.Id);
                await _orderEvents.AddAsync(
                    OrderEvent.Record(order.Id, OrderEventType.PARTIAL_REFUND_IGNORED, ScrubSensitiveFields(rawBody),
                        correlationId: correlationId),
                    ct);
            }
            else
            {
                _metrics.RecordWebhookReceived("tbank", "noop");
                _logger.LogDebug(
                    "T-Bank webhook noop: Order={OrderId} Status={TBankStatus}",
                    order.Id, notification.Status);
            }

            UnitResult<Error> commit = await _transactions.CommitTransactionAsync(ct);
            return commit.IsFailure ? commit.Error : UnitResult.Success<Error>();
        }

        IAsyncDisposable? refundLock = null;
        if (order.ChargeType == OrderChargeType.RENEWAL
            && string.Equals(mapping.NormalizedStatus, "REFUNDED", StringComparison.Ordinal))
        {
            Guid? refundGrantId = order.RenewalGrantId;
            if (refundGrantId is null)
            {
                Result<PlanGrant, Error> activeGrant = await _grants.GetByAsync(
                    g => g.UserId == order.UserId
                      && g.PlanId == order.PlanId
                      && g.RebillId == order.RebillId
                      && g.Status == PlanGrantStatus.ACTIVE,
                    ct);
                refundGrantId = activeGrant.IsSuccess ? activeGrant.Value.Id : null;
            }

            if (refundGrantId is not null)
            {
                refundLock = await _orders.TryAcquireRenewalLockAsync(refundGrantId.Value, ct);
                if (refundLock is null)
                {
                    return Error.Failure(
                        "tbank.webhook.renewal_lock_busy",
                        "Renewal refund will be retried after the active charge attempt finishes");
                }
            }
        }

        try
        {
        // Зовём существующий provider-agnostic handler (#85) — он MarkPaid/MarkFailed/Refund
        // и идэмпотентно выпускает PlanGrant. Все side-effects записываются в тот же
        // DbContext + outbox; финальный SaveChangesAsync ниже коммитит атомарно.
        PaymentWebhookRequest request = new(
            OrderId: order.Id,
            ExternalRef: paymentIdStr,
            Status: mapping.NormalizedStatus,
            Reason: mapping.Reason,
            // Recurring (#614): AUTHORIZED сохраняет RebillId на Order, а CONFIRMED может
            // продублировать его. Передаём значение из текущего notification либо сохранённый
            // fallback — generic handler привяжет token к subscription grant.
            RebillId: notification.RebillId);

        UnitResult<Error> handlerResult = await _genericHandler.Handle(request, ct);
        if (handlerResult.IsFailure)
        {
            _metrics.RecordWebhookReceived("tbank", "handler_error");
            _logger.LogError(
                "T-Bank webhook: PaymentWebhookHandler returned {Error} for OrderId={OrderId}",
                handlerResult.Error.Type, order.Id);
            // НЕ commit'им транзакцию: sub-handler делает MarkPaid/Refund перед read'ом
            // Plan/grant. Если read провалился (план удалён, grant race) — partial mutation
            // в Change Tracker'е. Commit зафиксировал бы Order.PAID без PlanGrant — юзер
            // заплатил без доступа. Rollback (через scope dispose) откатит всё.
            //
            // Endpoint вернёт 500 → T-Bank ретраит. Если состояние не разрешится за 24h —
            // reconciliation хард-эксп'ит Order через MaxPendingHoursBeforeExpire, а саппорт
            // обработает refund вручную.
            return Error.Failure("tbank.webhook.handler_failed", "Sub-handler failed");
        }

        // Audit на фактический результат: терминальный статус берём из order.Status (а не из
        // mapping) — generic handler мог пометить заказ FAILED через GRANT_FAILED даже на
        // PAID-уведомлении (план удалён). Так MARK_* всегда совпадает с реальным состоянием.
        OrderEventType eventType = order.Status switch
        {
            OrderStatus.PAID => OrderEventType.MARK_PAID,
            OrderStatus.FAILED => OrderEventType.MARK_FAILED,
            OrderStatus.REFUNDED => OrderEventType.REFUNDED,
            _ => OrderEventType.WEBHOOK_RECEIVED,
        };
        await _orderEvents.AddAsync(
            OrderEvent.Record(order.Id, eventType, ScrubSensitiveFields(rawBody), correlationId: correlationId),
            ct);

        // Atomic commit: domain-state (Order + PlanGrant) + audit OrderEvent + outbox
        // (PlanGrantCreated/Revoked) — всё в одной транзакции. Sub-handler делал
        // SaveChangesAsync без commit'а (transaction уже открыта); CommitTransactionAsync
        // зафлэшит всё атомарно.
        UnitResult<Error> finalCommit = await _transactions.CommitTransactionAsync(ct);
        if (finalCommit.IsFailure)
            return finalCommit.Error;

        _metrics.RecordWebhookReceived("tbank", "ok");
        _logger.LogInformation(
            "T-Bank webhook processed: Order={OrderId} Status={Status} (T-Bank={TBankStatus})",
            order.Id, mapping.NormalizedStatus, notification.Status);

        return UnitResult.Success<Error>();
        }
        finally
        {
            if (refundLock is not null)
            {
                await refundLock.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// T-Bank token = sha256 over ALL root-level scalar fields the notification
    /// actually contains (except <c>Token</c>), sorted by key (Ordinal) + the
    /// (Password, value) pair. Built from the RAW JSON, not a fixed DTO whitelist:
    /// T-Bank includes provider fields our <see cref="TBankNotification"/> doesn't
    /// model (e.g. <c>CardId</c>) and they MUST participate in the hash — otherwise
    /// every real notification fails verification (webhook 401 invalid_token).
    /// Nested objects (<c>DATA</c>, <c>Receipt</c>) and arrays/null don't participate.
    /// </summary>
    private bool VerifyToken(TBankNotification notification, string rawBody)
    {
        Dictionary<string, string> fields;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "Token", StringComparison.Ordinal))
                    continue;

                string? value = prop.Value.ValueKind switch
                {
                    // null JSON strings → GetString() == null → excluded by the null-check
                    // below (T-Bank omits null values from the token, same as nested/null).
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };
                if (value is not null)
                    fields[prop.Name] = value;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        bool ok = TBankSignature.VerifyToken(fields, _options.Password, notification.Token);
        if (!ok)
        {
            // Field NAMES only (no values) — safe to log, helps diagnose any future
            // mismatch without leaking Pan/token.
            _logger.LogDebug(
                "T-Bank token mismatch for OrderId={OrderId}: notification fields=[{Fields}]",
                notification.OrderId,
                string.Join(",", fields.Keys.OrderBy(k => k, StringComparer.Ordinal)));
        }
        return ok;
    }

    /// <summary>
    /// Truncate untrusted string before logging to defend against log-injection
    /// (huge / multiline values from a malformed webhook payload).
    /// </summary>
    private static string TruncateForLog(string value, int maxLen) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLen
            ? value
            : value[..maxLen] + "…";

    /// <summary>
    /// Удаляет PCI-restricted поля (`Pan`, `ExpDate`) из raw webhook body перед
    /// записью в <c>order_events.payload</c>. Маскированный PAN формально
    /// разрешён к хранению, но контракты с эквайером/CloudKassir и data
    /// minimization (152-ФЗ) требуют не складывать его в open-text JSON в БД.
    /// На malformed JSON возвращает <c>{}</c> — audit сохраняем без payload'а
    /// (signature валидация уже пройдена выше, payload — только для расследований).
    /// </summary>
    private static string ScrubSensitiveFields(string rawBody)
    {
        if (string.IsNullOrEmpty(rawBody))
            return rawBody;

        try
        {
            JsonNode? node = JsonNode.Parse(rawBody);
            if (node is not JsonObject obj)
                return rawBody;

            obj.Remove("Pan");
            obj.Remove("ExpDate");
            obj.Remove("Token");
            obj.Remove("RebillId");
            obj.Remove("CardId");
            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            return "{}";
        }
    }
}
