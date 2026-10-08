using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace PlatformAuth.HttpClients;

/// <summary>
///     Получает и кеширует JWT-токен через OAuth2 Client Credentials flow.
///     Используется для межсервисных вызовов, когда нет пользовательского контекста
///     (фоновые задачи, Wolverine handlers, scheduled jobs).
///     Регистрируется как Singleton — кеш токена общий для всех запросов.
/// </summary>
public sealed class ServiceTokenProvider : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ServiceTokenProvider> _logger;
    private readonly ServiceClientOptions _options;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ServiceTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<ServiceClientOptions> options,
        ILogger<ServiceTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public void Dispose() => _semaphore.Dispose();

    /// <summary>
    ///     Возвращает актуальный JWT-токен. Кеширует до истечения (с запасом 30 сек).
    ///     Возвращает ошибку, если client credentials не сконфигурированы или запрос неуспешен.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<Result<string, Error>> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return Error.Failure(
                "service.token.not_configured",
                "Service client credentials are not configured");
        }

        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            // Double-check после захвата семафора
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            return await RequestTokenAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<Result<string, Error>> RequestTokenAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 2;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            Result<string, Error> result = await RequestTokenOnceAsync(cancellationToken);

            if (result.IsSuccess
                || attempt == maxRetries
                || result.Error.Behavior != ErrorBehavior.Transient
                || cancellationToken.IsCancellationRequested)
                return result;

            int delayMs = 200 * (1 << attempt); // 200ms, 400ms
            _logger.LogWarning(
                "Service token request failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms",
                attempt + 1, maxRetries + 1, delayMs);

            await Task.Delay(delayMs, cancellationToken);
        }

        return Error.Failure("service.token.unavailable", "Сервис авторизации недоступен");
    }

    private async Task<Result<string, Error>> RequestTokenOnceAsync(CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent requestBody = new(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("scope", "openid service")
        ]);

        try
        {
            using HttpClient httpClient = _httpClientFactory.CreateClient();

            using HttpResponseMessage response = await httpClient.PostAsync(
                _options.TokenUrl,
                requestBody,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogError(
                    "Failed to obtain service token: {StatusCode} {Error}",
                    response.StatusCode,
                    errorBody);

                Error error = Error.Failure(
                    "service.token.request_failed",
                    $"Token request failed: {response.StatusCode}");

                return IsTransientStatusCode(response.StatusCode)
                    ? error.AsTransient()
                    : error;
            }

            TokenResponse? tokenResponse = await response.Content
                .ReadFromJsonAsync<TokenResponse>(cancellationToken);

            if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogError("Service token response is empty or missing access_token");

                return Error.Failure(
                    "service.token.empty_response",
                    "Token response is empty or missing access_token");
            }

            if (tokenResponse.ExpiresIn <= 0)
            {
                _logger.LogError(
                    "Service token response contains invalid expires_in: {ExpiresIn}",
                    tokenResponse.ExpiresIn);

                return Error.Failure(
                    "service.token.invalid_expiry",
                    "Token response contains a non-positive expires_in");
            }

            _cachedToken = tokenResponse.AccessToken;

            // Кешируем с запасом 30 секунд до истечения
            int bufferSeconds = Math.Min(30, tokenResponse.ExpiresIn / 2);
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - bufferSeconds);

            _logger.LogDebug(
                "Service token obtained, expires in {ExpiresIn}s (cached until {ExpiresAt})",
                tokenResponse.ExpiresIn,
                _expiresAt);

            return _cachedToken;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed while requesting service token from {TokenUrl}",
                _options.TokenUrl);

            return Error.Failure(
                "service.token.unavailable",
                "Token endpoint is unavailable").AsTransient();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Token request timed out for {TokenUrl}", _options.TokenUrl);

            return Error.Failure(
                "service.token.timeout",
                "Token endpoint request timed out").AsTransient();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while requesting service token from {TokenUrl}", _options.TokenUrl);

            return Error.Failure(
                "service.token.internal",
                "Failed to obtain service token");
        }
    }

    private static bool IsTransientStatusCode(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.RequestTimeout
        || statusCode == System.Net.HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }

        [JsonPropertyName("token_type")] public string TokenType { get; init; } = string.Empty;
    }
}
