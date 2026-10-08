using System.Diagnostics;

namespace SearchService.Core.Diagnostics;

/// <summary>
/// ActivitySource для SearchService. Кастомные spans оборачивают Typesense
/// search queries и реиндекс — видны в Tempo как часть полного trace'а.
///
/// Кастомные метрики (Counter/Histogram) намеренно не определяем — пока
/// что отслеживаем только built-in HTTP/EFCore метрики через OTel
/// auto-instrumentation. Бизнес-метрики добавим точечно когда понадобится.
/// </summary>
public static class SearchDiagnostics
{
    public const string SOURCE_NAME = "SearchService";

    public static readonly ActivitySource ActivitySource = new(SOURCE_NAME);
}
