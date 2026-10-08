using System.Diagnostics;

namespace ContentAccess;

/// <summary>
/// ActivitySource для Content Access подсистемы. Кастомные spans для
/// <c>CheckAccessAsync</c> / <c>CheckAccessBatchAsync</c> позволяют видеть
/// в Tempo Redis round-trip overhead на entitlement-проверках.
///
/// Подписка добавляется в <c>ObservabilityExtensions.AddObservability</c>
/// (одна конфигурация на все сервисы, которые вызывают <see cref="IEntitlementChecker"/>).
///
/// Кастомные метрики (Counter/Histogram) намеренно не определяем — пока
/// что отслеживаем только built-in HTTP/EFCore/Redis метрики через OTel
/// auto-instrumentation. Бизнес-метрики добавим точечно когда понадобится.
/// </summary>
public static class ContentAccessDiagnostics
{
    public const string SOURCE_NAME = "ContentAccess";

    public static readonly ActivitySource ActivitySource = new(SOURCE_NAME);
}
