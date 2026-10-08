using Microsoft.Extensions.Diagnostics.HealthChecks;
using Typesense;

namespace SearchService.Infrastructure.Typesense;

public sealed class TypesenseHealthCheck : IHealthCheck
{
    private readonly ITypesenseClient _typesenseClient;

    public TypesenseHealthCheck(ITypesenseClient typesenseClient)
    {
        _typesenseClient = typesenseClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            HealthResponse response = await _typesenseClient.RetrieveHealth(cancellationToken);

            return response.Ok
                ? HealthCheckResult.Healthy("Typesense connection is healthy")
                : HealthCheckResult.Unhealthy("Typesense health endpoint returned unhealthy status");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Typesense connection failed", ex);
        }
    }
}
