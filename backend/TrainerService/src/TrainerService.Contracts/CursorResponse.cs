namespace TrainerService.Contracts;

/// <summary>
///     Ответ с курсорной пагинацией (mirror CommentService). <see cref="NextCursor"/> = null,
///     когда страниц больше нет.
/// </summary>
/// <typeparam name="T">Тип элементов страницы.</typeparam>
public sealed record CursorResponse<T>
{
    /// <summary>Элементы текущей страницы.</summary>
    public required IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Курсор для следующей страницы; null когда больше нечего отдавать.</summary>
    public required string? NextCursor { get; init; }
}
