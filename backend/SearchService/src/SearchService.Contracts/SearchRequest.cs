using System.Diagnostics.CodeAnalysis;
using Common;

namespace SearchService.Contracts;

/// <summary>
/// Параметры запроса к поисковому индексу.
/// </summary>
public sealed class SearchRequest
{
    public SearchRequest(
        string search,
        int page = 1,
        int pageSize = 20,
        Guid? courseId = null,
        Guid[]? tagIds = null,
        EntityType[]? entityTypes = null,
        Guid? authorId = null,
        string? cursor = null,
        string? materialKind = null,
        string? accessFilter = null)
    {
        Search = search;
        Page = page;
        PageSize = pageSize;
        CourseId = courseId;
        TagIds = tagIds ?? [];
        EntityTypes = entityTypes ?? [];
        AuthorId = authorId;
        Cursor = cursor;
        MaterialKind = materialKind;
        AccessFilter = accessFilter;
    }

    /// <summary>Поисковая строка.</summary>
    public string Search { get; init; } = string.Empty;

    /// <summary>Номер страницы результатов.</summary>
    public int Page { get; init; }

    /// <summary>Количество результатов на странице.</summary>
    public int PageSize { get; init; }

    /// <summary>Опциональный фильтр поиска в пределах конкретного курса.</summary>
    public Guid? CourseId { get; init; }

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Required for ASP.NET Core AsParameters query array binding.")]
    /// <summary>Опциональный фильтр поиска по выбранным тегам.</summary>
    public Guid[] TagIds { get; init; } = [];

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Required for ASP.NET Core AsParameters query array binding.")]
    /// <summary>Опциональный фильтр поиска по типам документов.</summary>
    public EntityType[] EntityTypes { get; init; } = [];

    /// <summary>Опциональный фильтр поиска в пределах пространства автора.</summary>
    public Guid? AuthorId { get; init; }

    /// <summary>
    /// Keyset-курсор для browse-режима (сортировка по updated_at_ticks:desc). Декодируется
    /// как updated_at_ticks. Когда задан — Page игнорируется, выдача идёт через
    /// filter_by-boundary. Для relevance-поиска курсор не применяется (score ранжирования
    /// не монотонный — cursor-пагинация по нему невозможна).
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// Опциональный фильтр по kind материала (ARTICLE/VIDEO/NOTE/STREAM). Применяется
    /// только к entity_type=Material; на остальные документы не влияет.
    /// </summary>
    public string? MaterialKind { get; init; }

    /// <summary>
    /// Опциональный фильтр витрины: <c>"free"</c> возвращает PUBLIC или AUTHENTICATED,
    /// <c>"public"</c> — только PUBLIC, доступные без входа. Не зависит от grants
    /// вызывающего; недоступные хиты приходят с замком через <c>LockReasonResolver</c>.
    /// <c>null</c> — без фильтра.
    /// </summary>
    public string? AccessFilter { get; init; }
}
