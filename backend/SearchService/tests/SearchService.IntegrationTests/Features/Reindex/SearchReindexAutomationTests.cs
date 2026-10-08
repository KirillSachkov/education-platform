using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SearchService.Core.Features.Reindex.IntegrationEvents;
using SearchService.Core.Reindex;
using SearchService.Core.Reindex.State;
using SearchService.IntegrationTests.Infrastructure;
using SearchService.Web.Configuration;

namespace SearchService.IntegrationTests.Features.Reindex;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SearchReindexAutomationTests : SearchServiceTestsBase
{
    public SearchReindexAutomationTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetOrInitAsync_returns_initial_state_when_row_missing()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ISearchReindexStateRepository repo = scope.ServiceProvider
            .GetRequiredService<ISearchReindexStateRepository>();

        SearchReindexState state = await repo.GetOrInitAsync(CancellationToken.None);

        Assert.Equal(SearchReindexState.SINGLETON_ID, state.Id);
        Assert.Equal(0, state.AppliedGeneration);
        Assert.Null(state.LastAppliedAtUtc);
        Assert.Null(state.LastRequestId);
    }

    [Fact]
    public async Task MarkAppliedAsync_persists_generation_and_metadata()
    {
        Guid requestId = Guid.NewGuid();
        DateTime appliedAt = DateTime.UtcNow;

        await using (AsyncServiceScope writeScope = Services.CreateAsyncScope())
        {
            ISearchReindexStateRepository repo = writeScope.ServiceProvider
                .GetRequiredService<ISearchReindexStateRepository>();
            await repo.MarkAppliedAsync(7, "hash-abc", "release-1", requestId, appliedAt, CancellationToken.None);
        }

        await using AsyncServiceScope readScope = Services.CreateAsyncScope();
        ISearchReindexStateRepository readRepo = readScope.ServiceProvider
            .GetRequiredService<ISearchReindexStateRepository>();
        SearchReindexState state = await readRepo.GetOrInitAsync(CancellationToken.None);

        Assert.Equal(7, state.AppliedGeneration);
        Assert.Equal("hash-abc", state.AppliedSchemaHash);
        Assert.Equal("release-1", state.AppliedDeployStamp);
        Assert.Equal(requestId, state.LastRequestId);
        Assert.NotNull(state.LastAppliedAtUtc);
    }

    [Fact]
    public async Task Full_reindex_writes_applied_generation_to_db()
    {
        int configuredGeneration = ResolveConfiguredGeneration();

        await RunFullReindexAsync();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ISearchReindexStateRepository repo = scope.ServiceProvider
            .GetRequiredService<ISearchReindexStateRepository>();
        SearchReindexState state = await repo.GetOrInitAsync(CancellationToken.None);

        Assert.Equal(configuredGeneration, state.AppliedGeneration);
        // Handler фиксирует текущий schema-hash — startup-check после реиндекса молчит.
        Assert.Equal(ResolveSchemaHash(), state.AppliedSchemaHash);
        // DeployStamp в test-конфиге пуст → нормализуется в null.
        Assert.Null(state.AppliedDeployStamp);
        Assert.NotNull(state.LastAppliedAtUtc);
        Assert.NotNull(state.LastRequestId);
    }

    [Fact]
    public async Task Startup_service_publishes_full_reindex_when_generation_mismatches()
    {
        // applied = 0 (no row → init), configured = 1 (test config default) → mismatch.
        // Pattern A: TestOutboxService captures the publish synchronously in OutboxCollector;
        // no need for TrackActivity (which requires Wolverine durability persistence).
        SearchReindexOnStartupService service = ActivatorUtilities
            .CreateInstance<SearchReindexOnStartupService>(Services);
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        FullSearchReindexRequested? published =
            OutboxCollector.OfType<FullSearchReindexRequested>().FirstOrDefault();
        Assert.NotNull(published);
    }

    [Fact]
    public async Task Startup_service_skips_publish_when_generation_already_applied()
    {
        // applied = текущие generation + schema-hash; DeployStamp в test-конфиге пуст →
        // ни один из трёх сигналов не активен.
        await MarkAppliedCurrentAsync(DateTime.UtcNow);

        // Pattern A: call directly, no TrackActivity.
        SearchReindexOnStartupService service = ActivatorUtilities
            .CreateInstance<SearchReindexOnStartupService>(Services);
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        Assert.Empty(OutboxCollector.OfType<FullSearchReindexRequested>());
    }

    [Fact]
    public async Task Startup_service_skips_publish_on_generation_rollback_when_other_signals_quiet()
    {
        // Контракт (#526): config.ReindexGeneration < applied — generation-сигнал
        // игнорируется (rollback = операционная ошибка), но НЕ глушит schema-hash /
        // deploy-stamp. Здесь оба остальных сигнала неактивны → реиндекса нет.
        await using (AsyncServiceScope setup = Services.CreateAsyncScope())
        {
            ISearchReindexStateRepository repo = setup.ServiceProvider
                .GetRequiredService<ISearchReindexStateRepository>();
            await repo.MarkAppliedAsync(
                ResolveConfiguredGeneration() + 100,
                ResolveSchemaHash(),
                deployStamp: null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                CancellationToken.None);
        }

        SearchReindexOnStartupService service = ActivatorUtilities
            .CreateInstance<SearchReindexOnStartupService>(Services);
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        Assert.Empty(OutboxCollector.OfType<FullSearchReindexRequested>());
    }

    [Fact]
    public async Task Startup_service_publishes_on_generation_rollback_when_schema_hash_differs()
    {
        // Вторая половина контракта: rollback generation не блокирует авто-сигналы —
        // устаревший schema-hash всё равно триггерит реиндекс.
        await using (AsyncServiceScope setup = Services.CreateAsyncScope())
        {
            ISearchReindexStateRepository repo = setup.ServiceProvider
                .GetRequiredService<ISearchReindexStateRepository>();
            await repo.MarkAppliedAsync(
                ResolveConfiguredGeneration() + 100,
                "stale-schema-hash",
                deployStamp: null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                CancellationToken.None);
        }

        SearchReindexOnStartupService service = ActivatorUtilities
            .CreateInstance<SearchReindexOnStartupService>(Services);
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        Assert.NotNull(OutboxCollector.OfType<FullSearchReindexRequested>().FirstOrDefault());
    }

    [Fact]
    public async Task Startup_service_publishes_when_schema_hash_differs()
    {
        // generation совпадает, но applied_schema_hash от «прошлого билда» —
        // правка TypesenseSchemas меняет hash → реиндекс без ручного бампа (#526).
        await using (AsyncServiceScope setup = Services.CreateAsyncScope())
        {
            ISearchReindexStateRepository repo = setup.ServiceProvider
                .GetRequiredService<ISearchReindexStateRepository>();
            await repo.MarkAppliedAsync(
                ResolveConfiguredGeneration(),
                "stale-schema-hash",
                deployStamp: null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                CancellationToken.None);
        }

        SearchReindexOnStartupService service = ActivatorUtilities
            .CreateInstance<SearchReindexOnStartupService>(Services);
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        Assert.NotNull(OutboxCollector.OfType<FullSearchReindexRequested>().FirstOrDefault());
    }

    [Fact]
    public async Task Startup_service_publishes_when_deploy_stamp_differs()
    {
        // generation + schema-hash совпадают, но прилетел новый релиз (IMAGE_TAG) → реиндекс.
        await MarkAppliedCurrentAsync(DateTime.UtcNow, deployStamp: "release-1");

        SearchReindexOnStartupService service = CreateStartupService(deployStamp: "release-2");
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        Assert.NotNull(OutboxCollector.OfType<FullSearchReindexRequested>().FirstOrDefault());
    }

    [Fact]
    public async Task Startup_service_skips_publish_when_deploy_stamp_unchanged()
    {
        // Рестарт контейнера без деплоя: stamp тот же → no-op.
        await MarkAppliedCurrentAsync(DateTime.UtcNow, deployStamp: "release-1");

        SearchReindexOnStartupService service = CreateStartupService(deployStamp: "release-1");
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        Assert.Empty(OutboxCollector.OfType<FullSearchReindexRequested>());
    }

    [Fact]
    public async Task Reconciliation_service_skips_when_last_applied_too_recent()
    {
        // last applied 1 hour ago, MinIntervalSinceLast (test override) = 12h → skip
        await MarkAppliedCurrentAsync(DateTime.UtcNow.AddHours(-1));

        // Pattern A: call directly, no TrackActivity.
        SearchReindexReconciliationService service = ActivatorUtilities
            .CreateInstance<SearchReindexReconciliationService>(Services);
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        Assert.Empty(OutboxCollector.OfType<FullSearchReindexRequested>());
    }

    [Fact]
    public async Task Reconciliation_service_publishes_when_min_interval_elapsed()
    {
        await MarkAppliedCurrentAsync(DateTime.UtcNow.AddDays(-2));

        // Pattern A: call directly, no TrackActivity.
        SearchReindexReconciliationService service = ActivatorUtilities
            .CreateInstance<SearchReindexReconciliationService>(Services);
        await service.EvaluateAndTriggerAsync(CancellationToken.None);

        FullSearchReindexRequested? published =
            OutboxCollector.OfType<FullSearchReindexRequested>().FirstOrDefault();
        Assert.NotNull(published);
    }

    private int ResolveConfiguredGeneration()
    {
        using IServiceScope scope = Services.CreateScope();
        SearchReindexOptions options = scope.ServiceProvider
            .GetRequiredService<IOptions<SearchReindexOptions>>().Value;
        return options.ReindexGeneration;
    }

    private string ResolveSchemaHash()
    {
        using IServiceScope scope = Services.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<ISearchSchemaVersionProvider>().SchemaHash;
    }

    /// <summary>
    /// Расписывает applied-state «текущими» значениями (generation + schema-hash из DI),
    /// чтобы соответствующие startup-сигналы были неактивны.
    /// </summary>
    private async Task MarkAppliedCurrentAsync(DateTime appliedAtUtc, string? deployStamp = null)
    {
        await using AsyncServiceScope setup = Services.CreateAsyncScope();
        ISearchReindexStateRepository repo = setup.ServiceProvider
            .GetRequiredService<ISearchReindexStateRepository>();
        await repo.MarkAppliedAsync(
            ResolveConfiguredGeneration(),
            ResolveSchemaHash(),
            deployStamp,
            Guid.NewGuid(),
            appliedAtUtc,
            CancellationToken.None);
    }

    /// <summary>
    /// Startup-сервис с переопределённым DeployStamp (в test-конфиге stamp пуст).
    /// Остальные опции — из DI-конфига, чтобы generation-сигнал оставался неактивным.
    /// ВНИМАНИЕ: копирование пополевое — при добавлении нового поля в
    /// SearchReindexOptions дополни и этот список, иначе тест получит default.
    /// </summary>
    private SearchReindexOnStartupService CreateStartupService(string deployStamp)
    {
        SearchReindexOptions current;
        using (IServiceScope scope = Services.CreateScope())
        {
            current = scope.ServiceProvider.GetRequiredService<IOptions<SearchReindexOptions>>().Value;
        }

        IOptions<SearchReindexOptions> overridden = Options.Create(new SearchReindexOptions
        {
            ExportBatchSize = current.ExportBatchSize,
            ImportBatchSize = current.ImportBatchSize,
            DelayBetweenBatchesMs = current.DelayBetweenBatchesMs,
            ReindexGeneration = current.ReindexGeneration,
            ForceReindexOnStartup = current.ForceReindexOnStartup,
            Reconciliation = current.Reconciliation,
            DeployStamp = deployStamp,
        });

        return ActivatorUtilities.CreateInstance<SearchReindexOnStartupService>(Services, overridden);
    }
}
