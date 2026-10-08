namespace ContentAccess;

public interface IUserGrantWriter
{
    Task GrantAsync(Guid userId, string tag, CancellationToken ct = default);
    Task GrantManyAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default);
    Task RevokeAsync(Guid userId, string tag, CancellationToken ct = default);
    Task RevokeManyAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default);

    /// <summary>
    /// Атомарно заменяет полный набор тегов пользователя на <paramref name="tags"/>.
    /// Используется при revoke/expire grant'а — пересчитываем актуальный набор тегов
    /// из всех оставшихся ACTIVE grants пользователя и заменяем разом, чтобы не
    /// трогать теги, всё ещё покрытые другими grant'ами (multi-grant overlap).
    ///
    /// Пустой <paramref name="tags"/> => полностью очищает набор пользователя.
    /// </summary>
    Task ReplaceAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default);
}
