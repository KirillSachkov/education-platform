namespace TagService.Domain.Tags;

/// <summary>
/// Вид тега / Tag kind.
/// </summary>
public enum TagKind
{
    /// <summary>
    /// Канонический тег / canonical tag.
    /// </summary>
    CANON,

    /// <summary>
    /// Тег выступает алиасом другого тега / Tag acts as alias of another tag.
    /// </summary>
    ALIAS
}
