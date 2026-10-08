using System.Net.Http.Headers;
using FileService.Infrastructure.Kinescope;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FileService.Web.Configuration;

/// <summary>
///     Video uploads and reconciliation depend on Kinescope API availability and valid credentials.
/// </summary>
public sealed class KinescopeHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KinescopeOptions _options;

    public KinescopeHealthCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<KinescopeOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 5)));

            using HttpClient client = _httpClientFactory.CreateClient();
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"{_options.ApiBaseUrl}/v1/videos?per_page=1&parent_id={Uri.EscapeDataString(_options.ParentId)}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

            using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Kinescope API is reachable")
                : HealthCheckResult.Unhealthy($"Kinescope API returned {(int)response.StatusCode}");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Kinescope health check timed out", ex);
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy("Kinescope API request failed", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Kinescope health check failed", ex);
        }
    }
}
