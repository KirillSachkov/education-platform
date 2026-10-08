using Microsoft.Extensions.Options;
using SearchService.Core.Database;
using SearchService.Core.Features.Reindex.IntegrationEvents;
using SearchService.Core.Reindex;
using SearchService.Core.Reindex.State;

namespace SearchService.Web.Configuration;

/// <summary>
/// При старте процесса сравнивает три независимых сигнала с
/// <c>search.reindex_state</c> и при любом mismatch (или при
/// <see cref="SearchReindexOptions.ForceReindexOnStartup"/>) публикует
/// <see cref="FullSearchReindexRequested"/> через outbox — дальше Wolverine
/// прокатит реиндекс по штатному blue/green-пути. Сигналы (#526):
/// schema-hash (<see cref="ISearchSchemaVersionProvider"/> vs <c>applied_schema_hash</c>),
/// deploy-stamp (<see cref="SearchReindexOptions.DeployStamp"/> vs <c>applied_deploy_stamp</c>)
/// и legacy generation (<see cref="SearchReindexOptions.ReindexGeneration"/> vs
/// <c>applied_generation</c>, ручной kill-switch).
/// </summary>
/// <remarks>
/// Запускается как <see cref="BackgroundService"/>, но проверка выполняется
/// один раз и затем ExecuteAsync завершается. Малая задержка
/// (<see cref="STARTUP_DELAY"/>) даёт Wolverine HostedService подняться
/// раньше нашего publish'а — иначе outbox-flush не сможет довести message
/// до durable storage.
///
/// Multi-replica: сейчас SearchService запускается в одной реплике на проде,
/// поэтому простое сравнение допустимо. Когда понадобится 2+ реплик —
/// обернуть проверку в <c>pg_try_advisory_lock</c>, чтобы только один процесс
/// публиковал event.
/// </remarks>
public sealed class SearchReindexOnStartupService : BackgroundService
{
    private static readonly TimeSpan STARTUP_DELAY = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SearchReindexOptions _options;
    private readonly ISearchSchemaVersionProvider _schemaVersion;
    private readonly ILogger<SearchReindexOnStartupService> _logger;

    public SearchReindexOnStartupService(
        IServiceScopeFactory scopeFactory,
        IOptions<SearchReindexOptions> options,
        ISearchSchemaVersionProvider schemaVersion,
        ILogger<SearchReindexOnStartupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _schemaVersion = schemaVersion;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(STARTUP_DELAY, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await EvaluateAndTriggerAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            // Не валим процесс: реиндекс — projection, не критично к стартапу.
            // При следующем рестарте повторим попытку (applied_generation остаётся прежним).
            _logger.LogError(ex, "Search reindex startup check failed; service will continue without auto-reindex");
        }
    }

    /// <summary>
    /// Сравнивает конфиг с DB-состоянием и публикует <see cref="FullSearchReindexRequested"/>
    /// при mismatch'е. Внешне доступен для интеграционных тестов; в проде вызывается
    /// один раз из <see cref="ExecuteAsync"/>.
    /// </summary>
    public async Task EvaluateAndTriggerAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ISearchReindexStateRepository repository = scope.ServiceProvider
            .GetRequiredService<ISearchReindexStateRepository>();
        IOutboxService outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        SearchReindexState state = await repository.GetOrInitAsync(cancellationToken);

        // Сравнение по `>` (а не `!=`) — config-rollback (config < applied) НЕ триггерит
        // реиндекс по generation-сигналу. Откат версии = операционная ошибка,
        // авто-реиндекс по downgrade'у только запутает. Schema-hash / deploy-stamp
        // ниже сравниваются на строгое равенство и от направления не зависят.
        bool generationMismatch = _options.ReindexGeneration > state.AppliedGeneration;
        if (_options.ReindexGeneration < state.AppliedGeneration)
        {
            _logger.LogWarning(
                "Search reindex applied_generation ({Applied}) is ahead of configured ({Configured}). "
                + "Config rollback suspected; generation signal ignored.",
                state.AppliedGeneration,
                _options.ReindexGeneration);
        }

        // Авто-инвалидация (#526): hash схемы из кода vs последний применённый.
        // null в БД (фичу только что задеплоили / реиндекс ещё не проходил) = mismatch.
        bool schemaMismatch = !string.Equals(
            _schemaVersion.SchemaHash,
            state.AppliedSchemaHash,
            StringComparison.Ordinal);

        // Авто-инвалидация (#526): deploy-stamp релиза из CI. Пустой stamp
        // (dev/local, рестарт без деплоя) — сигнал неактивен.
        bool deployStampMismatch = !string.IsNullOrEmpty(_options.DeployStamp)
            && !string.Equals(_options.DeployStamp, state.AppliedDeployStamp, StringComparison.Ordinal);

        bool force = _options.ForceReindexOnStartup;

        if (!generationMismatch && !schemaMismatch && !deployStampMismatch && !force)
        {
            _logger.LogInformation(
                "Search reindex state up-to-date (generation={Generation}, schemaHash={SchemaHash}, deployStamp={DeployStamp}); no startup reindex needed",
                state.AppliedGeneration,
                state.AppliedSchemaHash,
                state.AppliedDeployStamp);
            return;
        }

        Guid requestId = Guid.CreateVersion7();
        DateTime requestedAtUtc = DateTime.UtcNow;

        await outbox.PublishAsync(
            new FullSearchReindexRequested(requestId, requestedAtUtc),
            cancellationToken);

        _logger.LogWarning(
            "Search reindex auto-triggered on startup. RequestId: {RequestId}, "
            + "GenerationMismatch: {GenerationMismatch}, SchemaMismatch: {SchemaMismatch}, "
            + "DeployStampMismatch: {DeployStampMismatch}, Force: {Force}",
            requestId,
            generationMismatch,
            schemaMismatch,
            deployStampMismatch,
            force);
    }
}
