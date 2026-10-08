namespace SearchService.Core.Reindex;

/// <summary>
/// Параметры полного reindex'а. По умолчанию настроены на «бережно к проду»:
/// маленькие батчи + задержка между ними, чтобы не перегрузить EducationContentService
/// и Typesense при 100k+ документов. Крутить через appsettings Reindex-секцию
/// или env ReindexOptions__ExportBatchSize и т.д.
/// </summary>
public sealed class SearchReindexOptions
{
    /// <summary>Сколько документов запрашивать у EducationContent одним cursor-запросом.</summary>
    public int ExportBatchSize { get; init; } = 200;

    /// <summary>Сколько документов отправлять в Typesense одним import-батчем.</summary>
    public int ImportBatchSize { get; init; } = 200;

    /// <summary>
    /// Пауза между батчами (мс). Даёт EducationContent/Typesense выдохнуть между
    /// cycles. На малых объёмах почти незаметна; на большой базе предотвращает
    /// pileup на SQL/HTTP threadpool. 0 = без задержки.
    /// </summary>
    public int DelayBetweenBatchesMs { get; init; } = 100;

    /// <summary>
    /// LEGACY ручной override «поколения» индекса (#526 — больше НЕ бампается при
    /// schema/денорм-правках; их покрывают <see cref="DeployStamp"/> и schema-hash).
    /// Оставлен как kill-switch: инкремент форсит один реиндекс без деплоя кода.
    /// На старте сервиса сравнивается с <c>search.reindex_state.applied_generation</c>;
    /// при <c>config &gt; applied</c> публикуется <c>FullSearchReindexRequested</c>.
    /// </summary>
    public int ReindexGeneration { get; init; } = 1;

    /// <summary>
    /// Deploy-stamp релиза (CI инжектит <c>IMAGE_TAG</c>/commit SHA через env
    /// <c>SearchReindexOptions__DeployStamp</c> в docker-compose.prod.yml).
    /// Непустой stamp, отличный от <c>search.reindex_state.applied_deploy_stamp</c>,
    /// триггерит один полный реиндекс на старте — любой код-чейндж (схема, фабрика
    /// документов, ECS-экспортёры, lock-resolver) едет только через деплой, поэтому
    /// stamp покрывает все категории из бывшего правила search-reindex.md разом.
    /// Пустая строка (dev/local, рестарт без деплоя) — триггер неактивен.
    /// </summary>
    public string DeployStamp { get; init; } = "";

    /// <summary>
    /// Disaster-recovery escape-hatch: триггернуть полный reindex на ближайшем
    /// старте процесса вне зависимости от состояния <c>applied_generation</c>.
    /// Снять после успешного завершения (иначе реиндекс будет крутиться каждый рестарт).
    /// </summary>
    public bool ForceReindexOnStartup { get; init; }

    /// <summary>
    /// Ночной reconciliation cron — защита от дрейфа, если live-event path
    /// что-то потерял (рестарт consumer'а в середине, broker hiccup, etc.).
    /// Не зависит от <see cref="ReindexGeneration"/>.
    /// </summary>
    public SearchReindexReconciliationOptions Reconciliation { get; init; } = new();
}

public sealed class SearchReindexReconciliationOptions
{
    /// <summary>Включён ли ночной reconciliation. По умолчанию выключен — opt-in.</summary>
    public bool Enabled { get; init; }

    /// <summary>Период между прогонами. Дефолт 24 часа.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Не запускать reconciliation, если предыдущий реиндекс завершился
    /// меньше этого времени назад. Защита от двойных прогонов при совпадении
    /// деплоя и cron-tick'а.
    /// </summary>
    public TimeSpan MinIntervalSinceLast { get; init; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Случайный jitter, добавляемый к первому тику после старта процесса.
    /// Чтобы при горизонтальном масштабировании несколько реплик не били
    /// reconciliation одновременно. Дефолт — 0 (одна реплика).
    /// </summary>
    public TimeSpan StartupJitter { get; init; } = TimeSpan.Zero;
}
