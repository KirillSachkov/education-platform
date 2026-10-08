using System.Diagnostics.CodeAnalysis;
using Common;

namespace SearchService.Contracts;

public sealed class GetDocumentsRequest
{
    public string? Search { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public Guid? CourseId { get; init; }

    /// <summary>
    /// Ограничить выдачу пространством автора. Когда пользователь ищет на /@slug/ странице,
    /// фронт подставляет этот параметр — выдача скоупится к автору.
    /// </summary>
    public Guid? AuthorId { get; init; }

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Required for ASP.NET Core AsParameters query array binding.")]
    public Guid[] TagIds { get; init; } = [];

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Required for ASP.NET Core AsParameters query array binding.")]
    public EntityType[] EntityTypes { get; init; } = [];

    /// <summary>
    /// Keyset-курсор для browse-режима. Base64 от updated_at_ticks.
    /// Когда передан — игнорируется Page и применяется sort_by=updated_at_ticks:desc.
    /// Для full-text search (search непустой) курсор игнорируется — там relevance ranking.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>Опциональный фильтр по kind материала (ARTICLE/VIDEO/NOTE/STREAM).</summary>
    public string? MaterialKind { get; init; }

    /// <summary>
    /// Витринный фильтр по уровню бесплатного доступа. Допустимые значения:
    /// <c>"free"</c> ограничивает выдачу PUBLIC и AUTHENTICATED документами,
    /// <c>"public"</c> — только PUBLIC; <c>null</c> — без фильтра.
    /// </summary>
    public string? AccessFilter { get; init; }
}
