using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TrainerService.Core.Database;
using TrainerService.Infrastructure.Postgres;

namespace TrainerService.Web.Configuration;

/// <summary>
///     CLI: <c>dotnet TrainerService.Web.dll snapshot-stats</c> (#681 T1). Runs ONE daily stat-snapshot
///     tick synchronously and exits — the same idempotent work the background job does, for tests/owner
///     to trigger on demand. Stands before <c>AddPlatformDefaults</c> in <c>Program.cs</c> (no HTTP/AI/Redis
///     startup; resolves the infra DbContext + repository itself).
/// </summary>
public static class SnapshotStatsCli
{
    public const string CommandName = "snapshot-stats";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, CommandName, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database connection string is not configured.");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog());
        services.AddSingleton(configuration);
        services.AddInfrastructurePostgres(configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<IStatSnapshotRepository>();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        StatSnapshotRunResult result = await repository.WriteSnapshotsAsync(today, cancellationToken);

        Log.Information(
            "snapshot-stats done for {Date}: {Daily} daily, {Mastery} mastery, {Question} question row(s)",
            today, result.DailyRows, result.TopicMasteryRows, result.QuestionAccuracyRows);
    }
}
