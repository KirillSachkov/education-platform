using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AccessService.Core.Features.Billing.Configuration;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.TBank.Contracts;
using Microsoft.Extensions.Options;

namespace AccessService.Core.Features.Billing.TBank;

/// <summary>
/// HTTP-клиент к T-Bank эквайрингу. Использует named HttpClient <c>"tbank"</c>
/// с Polly retry только для безопасных read-операций и общим CB
/// (см. <c>PaymentsRegistration.AddTBankClient</c>).
///
/// Token (sha256) проставляется на каждый запрос автоматически. Все запросы —
/// JSON POST к <c>{BaseUrl}/{operation}</c>. Никогда не идёт через nginx —
/// public host T-Bank.
/// </summary>
public sealed class TBankClient : ITBankClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Доверенные apex-домены T-Bank, на которых может жить платёжная форма (значение
    /// <c>PaymentURL</c> из Init). Принимаем сам apex и любой его поддомен: T-Bank отдаёт форму
    /// на РАЗНЫХ хостах (<c>securepayments.tinkoff.ru</c>, <c>pay.tbank.ru</c>, …) и активно
    /// мигрирует <c>tinkoff.ru</c> → <c>tbank.ru</c>. Фиксированный список конкретных хостов уже
    /// дважды ронял прод-оплату (#430 сверял PaymentURL host с API <see cref="TBankOptions.BaseUrl"/>
    /// <c>securepay.tinkoff.ru</c>; #440-allowlist не знал про реальный <c>pay.tbank.ru</c>),
    /// поэтому сверяемся именно с apex-доменом, а не с перечнем сабдоменов.
    /// </summary>
    private static readonly string[] TrustedPaymentApexDomains =
    [
        "tinkoff.ru",
        "tbank.ru",
    ];

    /// <summary>
    /// True, если <paramref name="host"/> — доверенный apex-домен T-Bank или его поддомен,
    /// либо pay.tbank-online.com — хост формы из production Init (#1170).
    /// Поддомен требует точку-разделитель (<c>".tbank.ru"</c>), поэтому open-redirect защита
    /// сохраняется: <c>eviltinkoff.ru</c> и <c>securepayments.tinkoff.ru.evil.com</c> не являются
    /// ни apex, ни его поддоменом → отвергаются.
    /// </summary>
    private static bool IsTrustedPaymentHost(string host) =>
        string.Equals(host, "pay.tbank-online.com", StringComparison.OrdinalIgnoreCase)
        || Array.Exists(
            TrustedPaymentApexDomains,
            apex => string.Equals(host, apex, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + apex, StringComparison.OrdinalIgnoreCase));

    private readonly HttpClient _http;
    private readonly TBankOptions _options;
    private readonly PaymentMetrics _metrics;
    private readonly ILogger<TBankClient> _logger;

    public TBankClient(
        HttpClient http,
        IOptions<TBankOptions> options,
        PaymentMetrics metrics,
        ILogger<TBankClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Result<TBankInitResponse, Error>> InitAsync(
        TBankInitRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Caller отвечает за заполнение всех полей кроме TerminalKey + Token —
        // их подписываем здесь. Это даёт чистую sep of concerns: use-case строит
        // payload, клиент подписывает + отправляет.
        request.TerminalKey = _options.TerminalKey;
        request.Token = ComputeInitToken(request);

        long startTicks = Stopwatch.GetTimestamp();
        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync("Init", request, JsonOpts, ct);
            double seconds = Stopwatch.GetElapsedTime(startTicks).TotalSeconds;
            _metrics.RecordInitDuration("tbank", seconds);

            if (!response.IsSuccessStatusCode)
            {
                _metrics.RecordInitOutcome("tbank", "http_error");
                _logger.LogWarning(
                    "T-Bank Init returned HTTP {Status} for OrderId={OrderId}",
                    (int)response.StatusCode,
                    request.OrderId);
                return TBankErrors.NetworkError($"HTTP {(int)response.StatusCode}");
            }

            TBankInitResponse? body = await response.Content.ReadFromJsonAsync<TBankInitResponse>(JsonOpts, ct);
            if (body is null)
            {
                _metrics.RecordInitOutcome("tbank", "invalid_response");
                return TBankErrors.InvalidResponse("body=null");
            }

            if (!body.Success)
            {
                _metrics.RecordInitOutcome("tbank", "provider_error");
                _logger.LogWarning(
                    "T-Bank Init failed: ErrorCode={ErrorCode} Message={Message} OrderId={OrderId}",
                    body.ErrorCode,
                    body.Message,
                    request.OrderId);
                return TBankErrors.InitFailed(body.ErrorCode, body.Message ?? body.Details);
            }

            if (string.IsNullOrEmpty(body.PaymentURL))
            {
                _metrics.RecordInitOutcome("tbank", "no_payment_url");
                return TBankErrors.MissingPaymentUrl();
            }

            // Open-redirect guard: фронт делает window.location.href = PaymentURL без проверок.
            // Требуем absolute https + host принадлежит доверенному apex-домену T-Bank (или его
            // поддомену) — иначе compromised/misconfigured ответ (data:/javascript:/чужой хост)
            // увёл бы юзера. ВАЖНО: платёжная форма T-Bank живёт на РАЗНЫХ сабдоменах
            // (securepayments.tinkoff.ru, pay.tbank.ru, …) и отличается от API BaseUrl
            // (securepay.tinkoff.ru). Фиксированный список хостов дважды ронял прод-оплату
            // (#430 сверял с BaseUrl; #440-allowlist не знал про pay.tbank.ru), поэтому сверяемся
            // с apex-доменом tinkoff.ru / tbank.ru и отдельным хостом pay.tbank-online.com
            // из production Init (#1170) — см. IsTrustedPaymentHost.
            if (!Uri.TryCreate(body.PaymentURL, UriKind.Absolute, out Uri? paymentUri)
                || !string.Equals(paymentUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || !IsTrustedPaymentHost(paymentUri.Host))
            {
                _metrics.RecordInitOutcome("tbank", "invalid_payment_url");
                _logger.LogWarning(
                    "T-Bank Init returned unexpected PaymentURL host for OrderId={OrderId}",
                    request.OrderId);
                return TBankErrors.UntrustedPaymentUrlHost();
            }

            _metrics.RecordInitOutcome("tbank", "ok");
            return body;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _metrics.RecordInitOutcome("tbank", "timeout");
            _logger.LogWarning(ex, "T-Bank Init timeout for OrderId={OrderId}", request.OrderId);
            return TBankErrors.NetworkError("timeout");
        }
        catch (Exception ex) when (IsPolicyTimeout(ex))
        {
            _metrics.RecordInitOutcome("tbank", "timeout");
            _logger.LogWarning(ex, "T-Bank Init timeout for OrderId={OrderId}", request.OrderId);
            return TBankErrors.NetworkError("timeout");
        }
        catch (HttpRequestException ex)
        {
            _metrics.RecordInitOutcome("tbank", "network_error");
            _logger.LogWarning(ex, "T-Bank Init network error for OrderId={OrderId}", request.OrderId);
            return TBankErrors.NetworkError(ex.Message);
        }
    }

    public async Task<Result<TBankGetStateResponse, Error>> GetStateAsync(
        string paymentId,
        CancellationToken ct = default)
    {
        return await PostSimpleAsync("GetState", paymentId, ct);
    }

    public async Task<Result<TBankCheckOrderResponse, Error>> CheckOrderAsync(
        string orderId,
        CancellationToken ct = default)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TerminalKey"] = _options.TerminalKey,
            ["OrderId"] = orderId,
        };
        var payload = new TBankCheckOrderRequest
        {
            TerminalKey = _options.TerminalKey,
            OrderId = orderId,
            Token = TBankSignature.ComputeToken(fields, _options.Password),
        };

        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                "CheckOrder", payload, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
            {
                TBankProviderErrorResponse? providerError = await TryReadProviderErrorAsync(response, ct);
                return providerError is { Success: false } && HasProviderErrorDetails(providerError)
                    ? TBankErrors.CheckOrderProviderError(providerError.ErrorCode, providerError.Message)
                    : TBankErrors.CheckOrderTransportError($"HTTP {(int)response.StatusCode}");
            }

            TBankCheckOrderResponse? body = await response.Content
                .ReadFromJsonAsync<TBankCheckOrderResponse>(JsonOpts, ct);
            if (body is null)
            {
                return TBankErrors.CheckOrderInvalidResponse("body=null");
            }

            if (!body.Success)
            {
                return TBankErrors.CheckOrderProviderError(body.ErrorCode, body.Message);
            }

            if (string.IsNullOrWhiteSpace(body.OrderId) || body.Payments is null)
            {
                return TBankErrors.CheckOrderInvalidResponse("missing OrderId or Payments");
            }

            if (body.Payments.Any(payment => string.IsNullOrWhiteSpace(payment.PaymentId)
                || string.IsNullOrWhiteSpace(payment.Status)))
            {
                return TBankErrors.CheckOrderInvalidResponse("payment is missing PaymentId or Status");
            }

            return body;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "T-Bank CheckOrder returned malformed JSON for OrderId={OrderId}", orderId);
            return TBankErrors.CheckOrderInvalidResponse("malformed JSON");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return TBankErrors.CheckOrderTransportError("timeout");
        }
        catch (Exception ex) when (IsPolicyTimeout(ex))
        {
            _logger.LogWarning(ex, "T-Bank CheckOrder timeout for OrderId={OrderId}", orderId);
            return TBankErrors.CheckOrderTransportError("timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "T-Bank CheckOrder network error for OrderId={OrderId}", orderId);
            return TBankErrors.CheckOrderTransportError(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<TBankCard>, Error>> GetCardListAsync(
        string customerKey,
        CancellationToken ct = default)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TerminalKey"] = _options.TerminalKey,
            ["CustomerKey"] = customerKey,
        };
        var payload = new TBankGetCardListRequest
        {
            TerminalKey = _options.TerminalKey,
            CustomerKey = customerKey,
            Token = TBankSignature.ComputeToken(fields, _options.Password),
        };

        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                "GetCardList", payload, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
            {
                TBankProviderErrorResponse? providerError = await TryReadProviderErrorAsync(response, ct);
                return providerError is { Success: false } && HasProviderErrorDetails(providerError)
                    ? TBankErrors.CardListProviderError(providerError.ErrorCode, providerError.Message)
                    : TBankErrors.CardListTransportError($"HTTP {(int)response.StatusCode}");
            }

            List<TBankCard>? cards = await response.Content
                .ReadFromJsonAsync<List<TBankCard>>(JsonOpts, ct);
            if (cards is null)
            {
                return TBankErrors.CardListInvalidResponse("body=null");
            }

            if (cards.Any(card => string.IsNullOrWhiteSpace(card.CardId)
                || string.IsNullOrWhiteSpace(card.Status)))
            {
                return TBankErrors.CardListInvalidResponse("card is missing CardId or Status");
            }

            return cards;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "T-Bank GetCardList returned malformed JSON for CustomerKey={CustomerKey}", customerKey);
            return TBankErrors.CardListInvalidResponse("malformed JSON");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return TBankErrors.CardListTransportError("timeout");
        }
        catch (Exception ex) when (IsPolicyTimeout(ex))
        {
            _logger.LogWarning(ex, "T-Bank GetCardList timeout for CustomerKey={CustomerKey}", customerKey);
            return TBankErrors.CardListTransportError("timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "T-Bank GetCardList network error for CustomerKey={CustomerKey}", customerKey);
            return TBankErrors.CardListTransportError(ex.Message);
        }
    }

    public async Task<Result<TBankChargeResponse, Error>> ChargeAsync(
        string paymentId,
        string rebillId,
        CancellationToken ct = default)
    {
        // Token подписывает TerminalKey + PaymentId + RebillId + Password (та же HMAC-схема,
        // что и InitAsync/PostSimpleAsync: sort Ordinal по ключам → concat values → sha256 hex).
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TerminalKey"] = _options.TerminalKey,
            ["PaymentId"] = paymentId,
            ["RebillId"] = rebillId,
        };
        string token = TBankSignature.ComputeToken(fields, _options.Password);
        var payload = new TBankChargeRequest
        {
            TerminalKey = _options.TerminalKey,
            PaymentId = paymentId,
            RebillId = rebillId,
            Token = token,
        };

        long startTicks = Stopwatch.GetTimestamp();
        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync("Charge", payload, JsonOpts, ct);
            double seconds = Stopwatch.GetElapsedTime(startTicks).TotalSeconds;
            _metrics.RecordChargeDuration("tbank", seconds);

            if (!response.IsSuccessStatusCode)
            {
                _metrics.RecordChargeOutcome("tbank", "http_error");
                _logger.LogWarning(
                    "T-Bank Charge returned HTTP {Status} for PaymentId={PaymentId}",
                    (int)response.StatusCode,
                    paymentId);
                return TBankErrors.NetworkError($"HTTP {(int)response.StatusCode}");
            }

            TBankChargeResponse? body = await response.Content.ReadFromJsonAsync<TBankChargeResponse>(JsonOpts, ct);
            if (body is null)
            {
                _metrics.RecordChargeOutcome("tbank", "invalid_response");
                return TBankErrors.InvalidResponse("body=null");
            }

            if (!body.Success)
            {
                _metrics.RecordChargeOutcome("tbank", "provider_error");
                _logger.LogWarning(
                    "T-Bank Charge failed: ErrorCode={ErrorCode} Status={Status} PaymentId={PaymentId}",
                    body.ErrorCode,
                    body.Status,
                    paymentId);
                return TBankErrors.ChargeFailed(body.ErrorCode, body.Message);
            }

            _metrics.RecordChargeOutcome("tbank", "ok");
            return body;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _metrics.RecordChargeOutcome("tbank", "timeout");
            _logger.LogWarning(ex, "T-Bank Charge timeout for PaymentId={PaymentId}", paymentId);
            return TBankErrors.NetworkError("timeout");
        }
        catch (Exception ex) when (IsPolicyTimeout(ex))
        {
            _metrics.RecordChargeOutcome("tbank", "timeout");
            _logger.LogWarning(ex, "T-Bank Charge timeout for PaymentId={PaymentId}", paymentId);
            return TBankErrors.NetworkError("timeout");
        }
        catch (HttpRequestException ex)
        {
            _metrics.RecordChargeOutcome("tbank", "network_error");
            _logger.LogWarning(ex, "T-Bank Charge network error for PaymentId={PaymentId}", paymentId);
            return TBankErrors.NetworkError(ex.Message);
        }
    }

    public async Task<Result<TBankGetStateResponse, Error>> CancelAsync(
        string paymentId,
        CancellationToken ct = default)
    {
        return await PostSimpleAsync("Cancel", paymentId, ct);
    }

    private async Task<Result<TBankGetStateResponse, Error>> PostSimpleAsync(
        string operation,
        string paymentId,
        CancellationToken ct)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TerminalKey"] = _options.TerminalKey,
            ["PaymentId"] = paymentId,
        };
        string token = TBankSignature.ComputeToken(fields, _options.Password);
        var payload = new TBankSimpleRequest
        {
            TerminalKey = _options.TerminalKey,
            PaymentId = paymentId,
            Token = token,
        };

        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(operation, payload, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
            {
                return TBankErrors.NetworkError($"HTTP {(int)response.StatusCode}");
            }

            TBankGetStateResponse? body = await response.Content.ReadFromJsonAsync<TBankGetStateResponse>(JsonOpts, ct);
            if (body is null)
            {
                return TBankErrors.InvalidResponse("body=null");
            }

            if (!body.Success)
            {
                return TBankErrors.InitFailed(body.ErrorCode, body.Message);
            }

            return body;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return TBankErrors.NetworkError("timeout");
        }
        catch (Exception ex) when (IsPolicyTimeout(ex))
        {
            return TBankErrors.NetworkError("timeout");
        }
        catch (HttpRequestException ex)
        {
            return TBankErrors.NetworkError(ex.Message);
        }
    }

    private static async Task<TBankProviderErrorResponse?> TryReadProviderErrorAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TBankProviderErrorResponse>(JsonOpts, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsPolicyTimeout(Exception exception) =>
        string.Equals(
            exception.GetType().Name,
            "TimeoutRejectedException",
            StringComparison.Ordinal);

    private static bool HasProviderErrorDetails(TBankProviderErrorResponse response) =>
        !string.IsNullOrWhiteSpace(response.ErrorCode)
        || !string.IsNullOrWhiteSpace(response.Message);

    /// <summary>
    /// Считает Init-токен. Поля: top-level scalar fields ONLY. Receipt и DATA
    /// исключены (T-Bank так требует). Token само поле тоже исключено.
    /// </summary>
    private string ComputeInitToken(TBankInitRequest request)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TerminalKey"] = request.TerminalKey,
            ["Amount"] = request.Amount.ToString(CultureInfo.InvariantCulture),
            ["OrderId"] = request.OrderId,
        };
        if (!string.IsNullOrEmpty(request.Description))
        {
            dict["Description"] = request.Description;
        }

        if (!string.IsNullOrEmpty(request.NotificationURL))
        {
            dict["NotificationURL"] = request.NotificationURL;
        }

        if (!string.IsNullOrEmpty(request.SuccessURL))
        {
            dict["SuccessURL"] = request.SuccessURL;
        }

        if (!string.IsNullOrEmpty(request.FailURL))
        {
            dict["FailURL"] = request.FailURL;
        }

        if (!string.IsNullOrEmpty(request.PayType))
        {
            dict["PayType"] = request.PayType;
        }

        // Все переданные top-level scalar fields участвуют в Init token. Только вложенные
        // DATA/Receipt и само поле Token исключаются по контракту T-Bank.
        if (!string.IsNullOrEmpty(request.CustomerKey))
        {
            dict["CustomerKey"] = request.CustomerKey;
        }

        if (!string.IsNullOrEmpty(request.Recurrent))
        {
            dict["Recurrent"] = request.Recurrent;
        }

        return TBankSignature.ComputeToken(dict, _options.Password);
    }

    private sealed class TBankSimpleRequest
    {
        [JsonPropertyName("TerminalKey")]
        public string TerminalKey { get; set; } = string.Empty;

        [JsonPropertyName("PaymentId")]
        public string PaymentId { get; set; } = string.Empty;

        [JsonPropertyName("Token")]
        public string Token { get; set; } = string.Empty;
    }

    private sealed class TBankCheckOrderRequest
    {
        [JsonPropertyName("TerminalKey")]
        public string TerminalKey { get; set; } = string.Empty;

        [JsonPropertyName("OrderId")]
        public string OrderId { get; set; } = string.Empty;

        [JsonPropertyName("Token")]
        public string Token { get; set; } = string.Empty;
    }

    private sealed class TBankGetCardListRequest
    {
        [JsonPropertyName("TerminalKey")]
        public string TerminalKey { get; set; } = string.Empty;

        [JsonPropertyName("CustomerKey")]
        public string CustomerKey { get; set; } = string.Empty;

        [JsonPropertyName("Token")]
        public string Token { get; set; } = string.Empty;
    }

    private sealed class TBankProviderErrorResponse
    {
        [JsonPropertyName("Success")]
        public bool Success { get; set; }

        [JsonPropertyName("ErrorCode")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("Message")]
        public string? Message { get; set; }
    }

    private sealed class TBankChargeRequest
    {
        [JsonPropertyName("TerminalKey")]
        public string TerminalKey { get; set; } = string.Empty;

        [JsonPropertyName("PaymentId")]
        public string PaymentId { get; set; } = string.Empty;

        [JsonPropertyName("RebillId")]
        public string RebillId { get; set; } = string.Empty;

        [JsonPropertyName("Token")]
        public string Token { get; set; } = string.Empty;
    }
}
