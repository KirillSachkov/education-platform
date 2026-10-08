namespace ContentAccess;

/// <summary>
/// Иммутабельный набор grant-тегов пользователя. Хранит теги как <see cref="Tags"/>
/// (для итерации/Typesense-фильтра), а внутри поддерживает <see cref="HashSet{T}"/>
/// для O(1) <see cref="Contains"/>. Построение один раз на загрузку из Redis — все
/// последующие проверки (LockReasonResolver, per-hit search) переиспользуют его.
/// </summary>
public sealed class EntitlementGrantSet
{
    public static readonly EntitlementGrantSet Empty = new([]);

    private readonly HashSet<string> _set;

    public EntitlementGrantSet(IReadOnlyList<string> tags)
    {
        Tags = tags ?? [];
        _set = new HashSet<string>(Tags, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Tags { get; }

    public int Count => _set.Count;

    public bool Contains(string tag) => _set.Contains(tag);
}
