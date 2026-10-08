using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TrainerService.Core.Database;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Infrastructure.Postgres;

namespace TrainerService.Web.Configuration;

/// <summary>
///     CLI: <c>dotnet TrainerService.Web.dll recompute-mastery</c> (#691). Idempotent backfill —
///     re-derives EVERY <c>topic_masteries</c> row from training-session-item history as the
///     difficulty-weighted average of the LATEST score per UNIQUE question (<see cref="MasteryCalculator"/>),
///     fixing already-inflated prod rows that were grown by the old farm-prone EWMA. Rows whose history no
///     longer backs them are reset to 0. Stands before <c>AddPlatformDefaults</c> in <c>Program.cs</c>
///     (no HTTP/AI/Redis startup; resolves the infra DbContext + repositories itself).
/// </summary>
public static class RecomputeMasteryCli
{
    public const string CommandName = "recompute-mastery";

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

        int rows = await RecomputeAllAsync(scope.ServiceProvider, cancellationToken);
        Log.Information("recompute-mastery done: {Rows} topic-mastery row(s) recomputed", rows);
    }

    /// <summary>
    ///     Recomputes every topic-mastery row from history into a single DbContext, then saves once.
    ///     Returns the number of mastery rows present afterwards. CLI/backfill context → plain DbContext
    ///     save (no domain events on the derived value), per the transactions rule's CLI exemption.
    /// </summary>
    public static async Task<int> RecomputeAllAsync(IServiceProvider provider, CancellationToken ct)
    {
        var sessions = provider.GetRequiredService<ITrainingSessionsRepository>();
        var masteryRepo = provider.GetRequiredService<ITopicMasteryRepository>();
        var dbContext = provider.GetRequiredService<TrainerServiceDbContext>();

        IReadOnlyList<UserTopicQuestionScore> history = await sessions.GetAllLatestScoredAnswersAsync(ct);
        IReadOnlyList<TopicMastery> existing = await masteryRepo.GetManyByAsync(_ => true, ct);

        Dictionary<(Guid UserId, Guid TopicId), TopicMastery> byKey =
            existing.ToDictionary(m => (m.UserId, m.TopicId));
        var recomputed = new HashSet<(Guid, Guid)>();

        foreach (var group in history.GroupBy(h => (h.UserId, h.TopicId)))
        {
            List<QuestionLatestScore> distinct = group
                .Select(h => new QuestionLatestScore(h.QuestionId, h.ScorePercent, h.Difficulty))
                .ToList();
            (int masteryPercent, int answersCount) = MasteryCalculator.Compute(distinct);
            DateTime practisedAt = group.Max(h => h.AnsweredAt);

            if (!byKey.TryGetValue(group.Key, out TopicMastery? mastery))
            {
                mastery = TopicMastery.Create(group.Key.UserId, group.Key.TopicId);
                await masteryRepo.AddAsync(mastery, ct);
                byKey[group.Key] = mastery;
            }

            mastery.SetDerived(masteryPercent, answersCount, practisedAt);
            recomputed.Add(group.Key);
        }

        // Rows with no scored history left → reset to 0 (kills already-inflated orphans). Keep the
        // existing practised-at marker — there is no fresh activity to date it from.
        foreach (TopicMastery mastery in existing)
        {
            if (!recomputed.Contains((mastery.UserId, mastery.TopicId)))
                mastery.SetDerived(0, 0, mastery.LastPractisedAt);
        }

        await dbContext.SaveChangesAsync(ct);
        return byKey.Count;
    }
}
