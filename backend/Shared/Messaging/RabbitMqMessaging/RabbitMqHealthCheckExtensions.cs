using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Shared.Messaging;

public static class RabbitMqHealthCheckExtensions
{
    /// <summary>
    ///     Safe регистрация RabbitMQ health check. При отсутствии <c>ConnectionStrings:RabbitMq</c>
    ///     регистрирует stub, возвращающий Unhealthy. Это важно для <c>dotnet ef migrations bundle</c>
    ///     в Docker build context, где env vars из compose ещё не доступны — eager <c>throw</c>
    ///     в ctor ломает migrations bundle. Runtime всё равно получит реальный connection string.
    /// </summary>
    public static IHealthChecksBuilder AddRabbitMqCheck(
        this IHealthChecksBuilder builder,
        IConfiguration configuration,
        string name = "rabbitmq")
    {
        string? connectionString = configuration.GetConnectionString("RabbitMq");

        if (string.IsNullOrWhiteSpace(connectionString) || !Uri.TryCreate(connectionString, UriKind.Absolute, out Uri? uri))
        {
            return builder.AddCheck(name, () =>
                HealthCheckResult.Unhealthy("ConnectionStrings:RabbitMq is not configured"));
        }

        return builder.AddCheck(name, new RabbitMqHealthCheck(uri));
    }
}
