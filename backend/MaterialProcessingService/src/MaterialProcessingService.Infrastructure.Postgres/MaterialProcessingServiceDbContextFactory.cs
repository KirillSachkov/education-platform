using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MaterialProcessingService.Infrastructure.Postgres;

public sealed class MaterialProcessingServiceDbContextFactory
    : IDesignTimeDbContextFactory<MaterialProcessingServiceDbContext>
{
#pragma warning disable S2068 // design-time-only fallback: local dev Postgres credentials, never used in runtime/prod
    private const string FALLBACK_CONNECTION_STRING =
        "Host=localhost;Port=5432;Database=education_platform;Username=postgres;Password=postgres";
#pragma warning restore S2068

    public MaterialProcessingServiceDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DATABASE") ??
            FALLBACK_CONNECTION_STRING;

        DbContextOptionsBuilder<MaterialProcessingServiceDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);

        return new MaterialProcessingServiceDbContext(optionsBuilder.Options);
    }
}
