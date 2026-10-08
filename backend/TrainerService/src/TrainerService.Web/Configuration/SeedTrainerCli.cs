using CSharpFunctionalExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SharedKernel;
using TrainerService.Infrastructure.Postgres;

namespace TrainerService.Web.Configuration;

/// <summary>
///     CLI-команда идемпотентного сидинга таксономии тренажёра (треки → темы → банки)
///     из <c>SeedData/trainer-seed.json</c>.
///
///     Usage: dotnet TrainerService.Web.dll seed-trainer [--force]
///
///     Повторный запуск безопасен: трек/тема, совпавшие по slug, по умолчанию НЕ трогаются
///     (лог + skip). Полная синхронизация мутабельных метаданных (title/area/description/
///     direction/stack) существующих треков/тем из seed-файла — только с явным <c>--force</c>.
///     Банки (опц.) создаются один раз по паре (тема, quizId) и никогда не апдейтятся.
///     При ошибке бросает исключение → процесс завершается ненулевым кодом.
/// </summary>
public static class SeedTrainerCli
{
    public const string CommandName = "seed-trainer";

    public const string FORCE_FLAG = "--force";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, CommandName, StringComparison.OrdinalIgnoreCase));

    public static bool HasForceFlag(string[] args) =>
        args.Any(x => string.Equals(x, FORCE_FLAG, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog());
        services.AddSingleton(configuration);
        services.AddSingleton(hostEnvironment);

        // Reuse the production wiring: DbContext + repositories + TransactionManager + domain events.
        services.AddInfrastructurePostgres(configuration);
        services.AddScoped<TrainerSeeder>();
        RecomputeFreeSamplesCli.AddRecomputeServices(services, configuration);

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

        TrainerSeeder seeder = scope.ServiceProvider.GetRequiredService<TrainerSeeder>();
        Result<TrainerSeedResult, Error> result = await seeder.SeedAsync(force, cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"seed-trainer failed: {result.Error.GetMessage()}");
        }

        // Free-sample backfill (#674): derive per-question IsFreeSample over the freshly seeded taxonomy
        // so the trainer is immediately usable for non-PRO users (no separate recompute step needed).
        int recomputedTopics = await RecomputeFreeSamplesCli.RecomputeAllTopicsAsync(
            scope.ServiceProvider, cancellationToken);
        Log.Information("seed-trainer free-sample recompute: {Topics} topic(s)", recomputedTopics);

        TrainerSeedResult summary = result.Value;
        Log.Information(
            "seed-trainer done: tracks +{TracksCreated}/~{TracksUpdated}/={TracksSkipped}, " +
            "topics +{TopicsCreated}/~{TopicsUpdated}/={TopicsSkipped}, banks +{BanksCreated}/={BanksSkipped}",
            summary.TracksCreated, summary.TracksUpdated, summary.TracksSkipped,
            summary.TopicsCreated, summary.TopicsUpdated, summary.TopicsSkipped,
            summary.BanksCreated, summary.BanksSkipped);
    }
}
