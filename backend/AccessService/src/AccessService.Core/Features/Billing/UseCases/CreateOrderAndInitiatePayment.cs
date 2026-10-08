using System.Text.Json;
using AccessService.Contracts.Billing;
using AccessService.Core.Database;
using AccessService.Core.Domain;
using AccessService.Core.Features.Billing.Configuration;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Core.Features.PlanGrants.Services;
using AccessService.Domain;
using AccessService.Domain.Billing;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Billing.UseCases;

/// <summary>
/// POST /access/orders/ — создаёт PENDING <see cref="Order"/>, вызывает <see cref="ITBankClient.InitAsync"/>,
/// привязывает PaymentId через <see cref="Order.AttachExternalRef"/> и возвращает <c>{orderId, paymentUrl}</c>.
///
/// Idempotency: header <c>Idempotency-Key</c> (UUID, генерируется фронтом на mount).
/// The durable state is scoped by (user, key): completed requests replay, while mismatched,
/// processing and failed requests fail closed without another provider Init.
///
/// Цена снэпшотится из <see cref="Plan.EffectivePriceCents"/> (с учётом активной акции)
/// МИНУС upgrade-credit за уже полученные планы (<see cref="IUpgradeCreditCalculator"/>, #486)
/// в <see cref="Order.AmountCents"/> в момент Create — клиенту никогда не доверяем сумму,
/// а показанная на pricing «К оплате N ₽» обязана совпасть со списанием.
/// Webhook (Phase F.1.2) сверит amount на mismatch.
/// </summary>
public sealed class CreateOrderEndpoint : IEndpoint
{
    public const string IDEMPOTENCY_HEADER = "Idempotency-Key";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Платформенный order-эндпоинт принимает ТОЛЬКО PLATFORM-scoped планы (#674) — trainer-оффер
        // покупается через /access/trainer-pro/orders (симметричный guard в handler'е по ExpectedScope).
        app.MapPost("/access/orders/", async Task<EndpointResult<CreateOrderResponse>> (
                [FromBody] CreateOrderRequest request,
                [FromServices] CreateOrderHandler handler,
                [FromServices] IIdempotencyKeyRepository idempotencyRepo,
                [FromServices] UserScopedData user,
                HttpContext httpContext,
                CancellationToken ct) =>
                await CreateOrderPipeline.ExecuteAsync(
                    request, PlanScope.PLATFORM, handler, idempotencyRepo, user, httpContext, ct))
            .RequireAuthorization()
            .RequireRateLimiting("order-create");
    }
}

/// <summary>
/// Общий идемпотентный конвейер создания заказа, разделяемый платформенным
/// (<see cref="CreateOrderEndpoint"/>) и тренажёрным (<c>/access/trainer-pro/orders</c>)
/// эндпоинтами. Единственное различие — <paramref name="expectedScope"/>: платформенный
/// принимает только PLATFORM-планы, тренажёрный — только TRAINER (#674). Логика order/payment
/// одна и та же (<see cref="CreateOrderHandler"/>), дублирования нет.
/// </summary>
public static class CreateOrderPipeline
{
    public static async Task<Result<CreateOrderResponse, Error>> ExecuteAsync(
        CreateOrderRequest request,
        PlanScope expectedScope,
        CreateOrderHandler handler,
        IIdempotencyKeyRepository idempotencyRepo,
        UserScopedData user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? idempotencyKey = httpContext.Request.Headers[CreateOrderEndpoint.IDEMPOTENCY_HEADER].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            idempotencyKey = null;
        }
        else if (idempotencyKey.Length > IdempotencyKey.KEY_MAX_LENGTH)
        {
            return Result.Failure<CreateOrderResponse, Error>(
                Error.Validation(
                    "idempotency.key.too_long",
                    $"Idempotency-Key должен быть не длиннее {IdempotencyKey.KEY_MAX_LENGTH} символов"));
        }

        if (idempotencyKey is not null)
        {
            IdempotencyKey? existing = await idempotencyRepo.GetByKeyAsync(
                user.UserId,
                idempotencyKey,
                ct);
            if (existing is not null)
            {
                if (existing.PlanId != request.PlanId || existing.ExpectedScope != expectedScope)
                {
                    return Error.Conflict(
                        "idempotency.request_mismatch",
                        "Этот Idempotency-Key уже использован для другого запроса");
                }

                if (existing.Status == IdempotencyKeyStatus.COMPLETED
                    && existing.ResponseBody is not null)
                {
                    CreateOrderResponse? replay =
                        JsonSerializer.Deserialize<CreateOrderResponse>(existing.ResponseBody);
                    if (replay is not null)
                    {
                        return replay;
                    }
                }

                return existing.Status is IdempotencyKeyStatus.FAILED or IdempotencyKeyStatus.RECOVERED
                    ? Error.Conflict(
                        existing.Status == IdempotencyKeyStatus.RECOVERED
                            ? "idempotency.recovered"
                            : "idempotency.failed",
                        existing.Status == IdempotencyKeyStatus.RECOVERED
                            ? "Предыдущая операция восстановлена в фоне; повтор с тем же ключом запрещён"
                            : "Предыдущая операция завершилась ошибкой; повтор с тем же ключом запрещён")
                    : Error.Conflict(
                        "idempotency.processing",
                        "Операция с этим ключом уже выполняется");
            }
        }

        return await handler.Handle(new CreateOrderCommand(request, idempotencyKey, expectedScope), ct);
    }
}

public sealed record CreateOrderCommand(
    CreateOrderRequest Request,
    string? IdempotencyKey,
    PlanScope ExpectedScope) : ICommand;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrderResponse, CreateOrderCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly IOrdersRepository _orders;
    private readonly IOrderEventsRepository _orderEvents;
    private readonly IIdempotencyKeyRepository _idempotency;
    private readonly IBillingConfigRepository _billingConfig;
    private readonly ITBankClient _tbank;
    private readonly TBankReceiptBuilder _receiptBuilder;
    private readonly TBankOptions _tbankOptions;
    private readonly BillingOptions _billingOptions;
    private readonly IAuthServiceClient _auth;
    private readonly IUpgradeCreditCalculator _creditCalculator;
    private readonly UserScopedData _user;
    private readonly ITransactionManager _transactions;
    private readonly PaymentMetrics _metrics;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        IOrdersRepository orders,
        IOrderEventsRepository orderEvents,
        IIdempotencyKeyRepository idempotency,
        IBillingConfigRepository billingConfig,
        ITBankClient tbank,
        TBankReceiptBuilder receiptBuilder,
        IOptions<TBankOptions> tbankOptions,
        IOptions<BillingOptions> billingOptions,
        IAuthServiceClient auth,
        IUpgradeCreditCalculator creditCalculator,
        UserScopedData user,
        ITransactionManager transactions,
        PaymentMetrics metrics,
        ILogger<CreateOrderHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(tbankOptions);
        ArgumentNullException.ThrowIfNull(billingOptions);

        _plans = plans;
        _grants = grants;
        _orders = orders;
        _orderEvents = orderEvents;
        _idempotency = idempotency;
        _billingConfig = billingConfig;
        _tbank = tbank;
        _receiptBuilder = receiptBuilder;
        _tbankOptions = tbankOptions.Value;
        _billingOptions = billingOptions.Value;
        _auth = auth;
        _creditCalculator = creditCalculator;
        _user = user;
        _transactions = transactions;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Result<CreateOrderResponse, Error>> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        // correlation_id (#443): OTel trace_id текущего запроса — пишем в Order и во все
        // init-audit события, чтобы расследование «оплатил, но доступа нет» шло от заказа
        // в админке прямо в логи/трейсы.
        string? correlationId = BillingCorrelation.CurrentTraceId();

        // Feature flag: billing отключён если T-Bank credentials не настроены.
        // Защита от случайного вывода в окружения без TBANK__* (например,
        // dev → main merge без валидных prod-ключей в Infisical).
        if (!_tbankOptions.IsConfigured)
        {
            _logger.LogWarning(
                "CreateOrder rejected: TBank credentials missing — billing disabled in this environment");
            return Error.Failure(
                "billing.not_configured",
                "Платёжная система временно недоступна. Свяжитесь с автором для получения доступа.");
        }

        // Runtime admin-toggle: даже при настроенном T-Bank приём оплаты можно
        // выключить тумблером в админке (issue #412). Resolve = ряд billing_config
        // либо дефолт из конфига (тот же путь, что в GetBillingConfig). Если выключен —
        // фронт показывает Telegram-fallback вместо кнопки «Оплатить».
        BillingConfig? billingConfig = await _billingConfig.GetAsync(cancellationToken);
        bool billingEnabled = billingConfig?.IsEnabled ?? _billingOptions.DefaultEnabled;
        if (!billingEnabled)
        {
            _logger.LogWarning("CreateOrder rejected: billing disabled via admin toggle");
            return Error.Validation(
                "billing.disabled",
                "Приём оплаты временно отключён");
        }

        // 1. Load Plan + validate state (active + public + has price + not archived).
        Result<Plan, Error> planResult = await _plans.GetByAsync(
            p => p.Id == command.Request.PlanId,
            cancellationToken);
        if (planResult.IsFailure)
        {
            return planResult.Error;
        }

        Plan plan = planResult.Value;

        // Symmetric scope guard (#674): platform order endpoint accepts only PLATFORM plans,
        // trainer-pro endpoint accepts only TRAINER plans. A mismatched plan id is rejected
        // before any state checks so the caller gets a clear "wrong endpoint" message.
        if (plan.Scope != command.ExpectedScope)
        {
            return command.ExpectedScope == PlanScope.PLATFORM
                ? AccessErrors.OrderTrainerScopeOnlyOnTrainerEndpoint()
                : AccessErrors.OrderPlatformScopeNotOnTrainerEndpoint();
        }

        if (plan.ArchivedAt is not null)
        {
            return Error.Validation("order.plan.archived", "План архивирован");
        }
        if (!plan.IsActive)
        {
            return Error.Validation("order.plan.inactive", "План неактивен");
        }
        if (!plan.IsPublic)
        {
            return Error.Validation("order.plan.not_public", "План недоступен для покупки");
        }
        if (plan.PriceCents is null or <= 0)
        {
            return Error.Validation("order.plan.no_price", "У плана не указана цена");
        }

        // Пробный план (#580) — один раз на пользователя. Блокируем повторную покупку,
        // если у юзера уже есть ACTIVE или EXPIRED grant этого trial-плана (REVOKED /
        // refunded → разрешаем заново). Иначе trial превращается в дешёвую помесячную
        // «подписку», что подрывает цель — продажу полного доступа.
        if (plan.IsTrial)
        {
            Guid buyerId = _user.UserId;
            Guid trialPlanId = plan.Id;
            bool trialUsed = await _grants.ExistsAsync(
                g => g.UserId == buyerId
                  && g.PlanId == trialPlanId
                  && (g.Status == PlanGrantStatus.ACTIVE || g.Status == PlanGrantStatus.EXPIRED),
                cancellationToken);
            if (trialUsed)
            {
                return AccessErrors.TrialAlreadyUsed();
            }
        }

        // 2. Create Order — snapshot the EFFECTIVE price (с учётом активной акции) МИНУС
        //    upgrade-credit за уже полученные планы (#486) в момент создания заказа (NEVER
        //    trust client). Quote и заказ считаются одним калькулятором — сумма «К оплате N ₽»
        //    на pricing-странице обязана совпасть со списанием. Цена фиксируется в Order: если
        //    акция закончится между Create и оплатой, юзер всё равно платит зафиксированную
        //    сумму, и webhook amount-match сходится (он сверяет Order.AmountCents, не пересчитывает).
        UpgradeQuote upgradeQuote = await _creditCalculator.CalculateAsync(
            _user.UserId,
            plan,
            cancellationToken);

        if (upgradeQuote.IsOwned)
        {
            return Error.Validation(
                "order.plan.already_owned",
                "У вас уже есть этот план — повторная покупка не нужна");
        }

        // FinalPriceCents null ⇔ OriginalPriceCents null ⇔ у плана нет цены — guard выше уже
        // отрезал этот случай, но инвариант делаем явным без null-suppress и без второго
        // снимка времени (security-review 2026-06-11: один now на quote и заказ).
        long? amountOrNull = upgradeQuote.FinalPriceCents ?? upgradeQuote.OriginalPriceCents;
        if (amountOrNull is not long amountCents)
        {
            return Error.Validation("order.plan.no_price", "У плана не указана цена");
        }

        // Сюда попадаем только при 100% credit (нулевая цена отрезана guard'ом order.plan.no_price).
        if (amountCents <= 0)
        {
            return Error.Validation(
                "order.nothing_to_pay",
                "Ваши предыдущие покупки уже покрывают стоимость этого плана — напишите автору, доступ откроют вручную");
        }
        Result<Order, Error> createOrder = Order.Create(
            userId: _user.UserId,
            planId: plan.Id,
            amountCents: amountCents,
            currency: plan.Currency,
            provider: "tbank",
            correlationId: correlationId);
        if (createOrder.IsFailure)
        {
            return createOrder.Error;
        }

        Order order = createOrder.Value;
        // Stage the Order in ChangeTracker. На сбое Init (#443) переводим его в FAILED и
        // сохраняем как audit-запись (раньше — early-return без save → заказ исчезал, и
        // админка не видела payment-init сбой до редиректа).
        await _orders.AddAsync(order, cancellationToken);

        using IDisposable? logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["orderId"] = order.Id,
            ["correlationId"] = correlationId,
        });

        // 3. Fetch user email (best-effort — falls back to TBankReceiptOptions.DefaultEmail
        // через TBankReceiptBuilder).
        string? userEmail = null;
        Result<IReadOnlyList<AuthUserLookupDto>, Error> userResult =
            await _auth.GetUsersByIdsAsync(new[] { _user.UserId }, cancellationToken);
        if (userResult.IsSuccess)
        {
            userEmail = userResult.Value.FirstOrDefault(u => u.UserId == _user.UserId)?.Email;
        }
        else
        {
            _logger.LogWarning(
                "AuthService user lookup failed for receipt email, falling back to default. UserId={UserId} Error={Error}",
                _user.UserId,
                userResult.Error.GetMessage());
        }

        // 4. Build T-Bank Init request. TerminalKey + Token заполняет TBankClient.
        string paymentDescription = $"Доступ к плану «{plan.DisplayName.Value}»";
        if (paymentDescription.Length > TBankInitRequest.DESCRIPTION_MAX_LENGTH)
        {
            paymentDescription = paymentDescription[..TBankInitRequest.DESCRIPTION_MAX_LENGTH];
        }

        TBankInitRequest initRequest = new()
        {
            Amount = order.AmountCents,
            OrderId = order.Id.ToString(),
            Description = paymentDescription,
            NotificationURL = _tbankOptions.NotificationUrl,
            SuccessURL = _tbankOptions.SuccessUrl,
            FailURL = _tbankOptions.FailUrl,
            PayType = "O", // одностадийный — списание сразу
            DATA = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["userId"] = _user.UserId.ToString(),
                ["planId"] = plan.Id.ToString(),
                ["OperationInitiatorType"] = "0",
            },
            Receipt = _receiptBuilder.Build(plan, userEmail, order.AmountCents),
        };

        // Подписка (#614): родительский рекуррентный платёж. Recurrent="Y" + стабильный
        // CustomerKey (строка userId) — T-Bank вернёт RebillId в webhook'е AUTHORIZED, который
        // мы сохраним до CONFIRMED для безредиректных автосписаний. Для
        // не-подписочных планов поля остаются null (обычный разовый платёж).
        if (plan.Tier == PlanTier.SUBSCRIPTION)
        {
            initRequest.Recurrent = "Y";
            initRequest.CustomerKey = _user.UserId.ToString();
            initRequest.DATA["OperationInitiatorType"] = "1";
        }

        // 5. Audit: about to call provider Init.
        await _orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                OrderEventType.INIT_CALLED,
                JsonSerializer.Serialize(new { provider = "tbank", amountCents = order.AmountCents }),
                correlationId: correlationId),
            cancellationToken);

        IdempotencyKey? reservation = null;
        if (command.IdempotencyKey is not null)
        {
            reservation = IdempotencyKey.CreateProcessing(
                command.IdempotencyKey,
                _user.UserId,
                plan.Id,
                command.ExpectedScope,
                order.Id);
            await _idempotency.AddAsync(reservation, cancellationToken);
        }

        // Commit the durable reservation, PENDING order and INIT_CALLED audit atomically.
        // This transaction is complete before the external HTTP call below.
        UnitResult<Error> reserveSave = await _transactions.SaveChangesAsync(cancellationToken);
        if (reserveSave.IsFailure)
        {
            return reserveSave.Error.Type == ErrorType.CONFLICT && reservation is not null
                ? Error.Conflict(
                    "idempotency.processing",
                    "Операция с этим ключом уже выполняется")
                : reserveSave.Error;
        }

        // 6. Call T-Bank Init. На сбое — persist FAILED + INIT_FAILED/INIT_URL_REJECTED (#443),
        //    чтобы админка видела init-сбой ДО редиректа, а не молчаливый rollback.
        Result<TBankInitResponse, Error> initResult = await _tbank.InitAsync(initRequest, cancellationToken);
        if (initResult.IsFailure)
        {
            if (HasErrorCode(initResult.Error, "tbank.network.error"))
            {
                return await PersistAmbiguousInitFailureAsync(
                    order, initResult.Error, correlationId, cancellationToken);
            }

            return await PersistInitFailureAsync(
                order, reservation, initResult.Error, correlationId, cancellationToken);
        }

        TBankInitResponse initResponse = initResult.Value;
        // PaymentURL is already validated by TBankClient; only PaymentId remains to check here.
        if (string.IsNullOrEmpty(initResponse.PaymentId))
        {
            return await PersistInitFailureAsync(
                order,
                reservation,
                TBankErrors.InvalidResponse("missing PaymentId"),
                correlationId,
                cancellationToken);
        }
        if (string.IsNullOrEmpty(initResponse.PaymentURL))
        {
            // Defensive: TBankClient guarantees non-empty PaymentURL on success, but the
            // DTO type allows null. Keep this for the nullable-flow analyzer.
            return await PersistInitFailureAsync(
                order,
                reservation,
                TBankErrors.MissingPaymentUrl(),
                correlationId,
                cancellationToken);
        }

        // 7. Attach external ref + audit success.
        UnitResult<Error> attach = order.AttachExternalRef(initResponse.PaymentId);
        if (attach.IsFailure)
        {
            return attach.Error;
        }

        await _orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                OrderEventType.INIT_RESPONDED,
                JsonSerializer.Serialize(new { provider = "tbank" }),
                correlationId: correlationId),
            cancellationToken);

        CreateOrderResponse response = new(order.Id, initResponse.PaymentURL);
        if (reservation is not null)
        {
            string serialized = JsonSerializer.Serialize(response);
            UnitResult<Error> complete = reservation.Complete(serialized);
            if (complete.IsFailure)
            {
                return complete.Error;
            }
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _metrics.RecordOrderCreated("tbank", plan.Tier.ToString());

        _logger.LogInformation(
            "Order {OrderId} created for User={UserId} Plan={PlanId} Amount={Amount}",
            order.Id,
            _user.UserId,
            plan.Id,
            order.AmountCents);

        return response;
    }

    /// <summary>
    /// A timeout/network failure does not prove that provider Init failed: the bank may have
    /// accepted OrderId before the response was lost. Keep the durable order PENDING and the
    /// idempotency reservation PROCESSING so reconciliation can recover it through CheckOrder;
    /// a retry with the same key therefore cannot issue a second Init.
    /// </summary>
    private async Task<Result<CreateOrderResponse, Error>> PersistAmbiguousInitFailureAsync(
        Order order,
        Error initError,
        string? correlationId,
        CancellationToken ct)
    {
        string code = initError.Messages.Count > 0
            ? initError.Messages[0].Code
            : "tbank.network.error";
        string message = initError.GetMessage();
        if (message.Length > 300)
        {
            message = message[..300];
        }

        _logger.LogWarning(
            "T-Bank Init result is ambiguous for Order {OrderId}; leaving PENDING for CheckOrder recovery",
            order.Id);
        await _orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                OrderEventType.INIT_FAILED,
                JsonSerializer.Serialize(new { code, message, ambiguous = true }),
                correlationId: correlationId),
            ct);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        return save.IsFailure ? save.Error : initError;
    }

    /// <summary>
    /// Персистит заказ как FAILED + INIT_FAILED/INIT_URL_REJECTED audit-row на сбое Init (#443).
    /// Сохраняет только безопасный snapshot (technical code + sanitized message) — никаких
    /// raw payload'ов T-Bank, card data, токенов или полного PaymentURL. Возвращает исходную
    /// ошибку — пользователь всё равно видит, что заказ не удался.
    /// </summary>
    private async Task<Result<CreateOrderResponse, Error>> PersistInitFailureAsync(
        Order order,
        IdempotencyKey? reservation,
        Error initError,
        string? correlationId,
        CancellationToken ct)
    {
        string code = initError.Messages.Count > 0 ? initError.Messages[0].Code : "tbank.init.failed";
        bool urlRejected = string.Equals(code, "tbank.response.untrusted_host", StringComparison.Ordinal);
        OrderEventType eventType = urlRejected ? OrderEventType.INIT_URL_REJECTED : OrderEventType.INIT_FAILED;

        string message = initError.GetMessage();
        if (message.Length > 300)
        {
            message = message[..300];
        }

        _logger.LogWarning("T-Bank Init failed: code={Code} event={EventType}", code, eventType);

        // Order сейчас PENDING (staged) — переводим в FAILED с коротким техническим reason'ом.
        order.MarkFailed(code);
        if (reservation is not null)
        {
            UnitResult<Error> fail = reservation.Fail();
            if (fail.IsFailure)
            {
                return fail.Error;
            }
        }

        await _orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                eventType,
                JsonSerializer.Serialize(new { code, message }),
                correlationId: correlationId),
            ct);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        return save.IsFailure ? save.Error : initError;
    }

    private static bool HasErrorCode(Error error, string code) =>
        error.Messages.Any(message => string.Equals(message.Code, code, StringComparison.Ordinal));
}
