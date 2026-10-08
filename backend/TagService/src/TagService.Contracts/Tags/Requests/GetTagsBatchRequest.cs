using System.Diagnostics.CodeAnalysis;

namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на получение массива тегов по идентификаторам / Request to get tags by identifiers.
/// </summary>
public sealed record GetTagsBatchRequest
{
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Required for ASP.NET Core AsParameters query array binding.")]
    public Guid[] TagIds { get; init; } = [];

    /// <summary>
    /// Фильтр по автору / Filter by author.
    /// </summary>
    public Guid? AuthorId { get; init; }
}