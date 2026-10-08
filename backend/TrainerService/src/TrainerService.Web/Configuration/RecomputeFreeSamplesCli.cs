using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TrainerService.Core.Configuration;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain.Topics;
using TrainerService.Infrastructure.Postgres;

namespace TrainerService.Web.Configuration;

/// <summary>
///     CLI: <c>dotnet TrainerService.Web.dll recompute-free-samples</c> (#674). Idempotent backfill —
///     iterates every topic and re-derives per-question <c>IsFreeSample</c> via
///     <c>TrainerFreeAllocationPolicy</c>. Run post-deploy after the migration (existing rows ship
///     <c>is_free_sample=false</c> = locked until this runs) and any time the <c>FreeSamplePercent</c>
///     knob changes. Stands before <c>AddPlatformDefaults</c> in <c>Program.cs</c> (no HTTP/AI/Redis
///     startup; resolves repositories + DbContext itself).
/// </summary>
public static class RecomputeFreeSamplesCli
{
    public const string CommandName = "recompute-free-samples";

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
        AddRecomputeServices(services, configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        int topics = await RecomputeAllTopicsAsync(scope.ServiceProvider, cancellationToken);
        Log.Information("recompute-free-samples done: {Topics} topic(s) recomputed", topics);
    }

    /// <summary>
    ///     Registers the free-sample recomputer + its options (assumes infra/DbContext already added).
    ///     Reused by the <c>seed-trainer</c> CLI for its post-seed recompute pass.
    /// </summary>
    public static void AddRecomputeServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TrainerOptions>(configuration.GetSection(TrainerOptions.SECTION_NAME));
        services.AddScoped<TrainerFreeSampleRecomputer>();
    }

    /// <summary>
    ///     Recomputes the free-sample allocation for EVERY topic into a single DbContext, then saves once.
    ///     Returns the number of topics processed. CLI/backfill context → plain DbContext save (no domain
    ///     events on the flag), per the transactions rule's CLI exemption.
    /// </summary>
    public static async Task<int> RecomputeAllTopicsAsync(IServiceProvider provider, CancellationToken ct)
    {
        var topicsRepo = provider.GetRequiredService<ITopicsRepository>();
        var recomputer = provider.GetRequiredService<TrainerFreeSampleRecomputer>();
        var dbContext = provider.GetRequiredService<TrainerServiceDbContext>();

        IReadOnlyList<Topic> topics = await topicsRepo.GetManyByAsync(_ => true, ct);
        foreach (Topic topic in topics)
            await recomputer.RecomputeForTopicAsync(topic.Id, ct: ct);

        await dbContext.SaveChangesAsync(ct);
        return topics.Count;
    }
}
