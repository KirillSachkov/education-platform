using Microsoft.EntityFrameworkCore.Design;

namespace AccessService.Infrastructure.Postgres;

/// <summary>
/// Keeps EF migration tooling independent from the web host and its OpenAPI/runtime-only
/// dependencies. The migrations bundle reads the real connection from the container env.
/// </summary>
public sealed class AccessServiceDbContextFactory
    : IDesignTimeDbContextFactory<AccessServiceDbContext>
{
#pragma warning disable S2068 // design-time-only local fallback; runtime uses environment configuration
    private const string FALLBACK_CONNECTION_STRING =
        "Host=localhost;Port=5432;Database=education_platform;Username=postgres;Password=postgres";
#pragma warning restore S2068

    public AccessServiceDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DATABASE")
            ?? FALLBACK_CONNECTION_STRING;

        DbContextOptionsBuilder<AccessServiceDbContext> options = new();
        options.UseNpgsql(connectionString);
        return new AccessServiceDbContext(options.Options);
    }
}
