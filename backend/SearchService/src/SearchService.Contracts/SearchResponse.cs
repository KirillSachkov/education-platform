namespace SearchService.Contracts;

/// <summary>
/// Ответ поискового сервиса с результатами, фасетами и информацией о пагинации.
/// </summary>
/// <param name="hits">Список найденных документов.</param>
/// <param name="facets">Список фасетов для построения фильтров.</param>
/// <param name="totalCount">Общее количество документов, удовлетворяющих запросу.</param>
/// <param name="page">Текущая страница результатов.</param>
/// <param name="pageSize">Размер страницы результатов.</param>
/// <typeparam name="TDocument">Тип документа в поисковом индексе.</typeparam>
public sealed record SearchResponse<TDocument>
{
    public SearchResponse(
        IReadOnlyList<SearchHit<TDocument>> hits,
        IReadOnlyList<SearchFacet> facets,
        int totalCount,
        int page,
        int pageSize,
        string? nextCursor = null)
    {
        Hits = hits;
        Facets = facets;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
        NextCursor = nextCursor;
    }

    public IReadOnlyList<SearchHit<TDocument>> Hits { get; init; }

    public IReadOnlyList<SearchFacet> Facets { get; init; }

    public int TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>
    /// Курсор для следующей страницы в browse-режиме. Null, когда либо keyset не запрашивался,
    /// либо достигнут конец выдачи.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// Элемент выдачи поиска с документом, оценкой релевантности и подсветкой совпадений.
/// </summary>
/// <param name="document">Найденный документ.</param>
/// <param name="score">Оценка релевантности документа (если поддерживается движком поиска).</param>
/// <param name="highlights">Подсвеченные фрагменты текста по совпавшим полям.</param>
/// <typeparam name="TDocument">Тип найденного документа.</typeparam>
public sealed record SearchHit<TDocument>
{
    public SearchHit(
        TDocument document,
        long? score,
        IReadOnlyList<SearchHighlight> highlights)
    {
        Document = document;
        Score = score;
        Highlights = highlights;
    }

    public TDocument Document { get; init; }

    public long? Score { get; init; }

    public IReadOnlyList<SearchHighlight> Highlights { get; init; }
}

/// <summary>
/// Подсвеченный фрагмент текста для конкретного поля документа.
/// </summary>
/// <param name="field">Имя поля документа.</param>
/// <param name="snippet">Фрагмент текста с выделенными совпадениями.</param>
/// <param name="matchedIndices">
///     Индексы совпавших элементов для string[]-полей (например <c>chapter_titles</c>) —
///     приходят из Typesense <c>highlight.indices</c>. Параллельны позициям в
///     соответствующих parallel-массивах документа (<c>chapter_titles[i]</c> ↔
///     <c>chapter_timestamps[i]</c>). Для скалярных полей <c>null</c>.
/// </param>
public sealed record SearchHighlight
{
    public SearchHighlight(string field, string snippet, IReadOnlyList<int>? matchedIndices = null)
    {
        Field = field;
        Snippet = snippet;
        MatchedIndices = matchedIndices;
    }

    public string Field { get; init; }

    public string Snippet { get; init; }

    public IReadOnlyList<int>? MatchedIndices { get; init; }
}

/// <summary>
/// Фасет по полю документа с распределением значений.
/// </summary>
/// <param name="field">Имя поля, по которому рассчитан фасет.</param>
/// <param name="values">Список значений фасета и количества документов для каждого значения.</param>
public sealed record SearchFacet
{
    public SearchFacet(string field, IReadOnlyList<SearchFacetValue> values)
    {
        Field = field;
        Values = values;
    }

    public string Field { get; init; }

    public IReadOnlyList<SearchFacetValue> Values { get; init; }
}

/// <summary>
/// Значение фасета и количество документов, в которых оно встречается.
/// </summary>
/// <param name="value">Значение фасета.</param>
/// <param name="count">Количество документов с данным значением.</param>
public sealed record SearchFacetValue
{
    public SearchFacetValue(string value, int count)
    {
        Value = value;
        Count = count;
    }

    public string Value { get; init; }

    public int Count { get; init; }
}
