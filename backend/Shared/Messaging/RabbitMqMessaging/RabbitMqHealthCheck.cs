using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Shared.Messaging;

public sealed class RabbitMqHealthCheck : IHealthCheck, IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;

    public RabbitMqHealthCheck(Uri connectionUri)
    {
        _factory = new ConnectionFactory
        {
            Uri = connectionUri,
            AutomaticRecoveryEnabled = false,
        };
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_connection is null || !_connection.IsOpen)
            {
                if (_connection is not null)
                {
                    await _connection.DisposeAsync();
                }

                _connection = await _factory.CreateConnectionAsync(cancellationToken);
            }

            return _connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ connection is open")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is not open");
        }
        catch (Exception ex)
        {
            _connection = null;
            return HealthCheckResult.Unhealthy("RabbitMQ connection failed", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
