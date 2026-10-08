namespace AuthService.Core.Database;

/// <summary>
///     Кэш GitHub-организаций пользователя. Read-side — Dapper, write-side — bulk SQL UPSERT/DELETE.
/// </summary>
public interface IUserGithubOrgRepository
{
    /// <summary>Возвращает список org-slug'ов пользователя.</summary>
    Task<IReadOnlyList<string>> GetByUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    ///     Полный пересинк: upsert переданного набора + удаление org-slug'ов, которых больше нет.
    ///     Идемпотентно. Делает один UPSERT и один DELETE.
    /// </summary>
    Task ReplaceAllAsync(
        Guid userId,
        IReadOnlyCollection<string> orgSlugs,
        DateTime syncedAt,
        CancellationToken ct);

    /// <summary>Возвращает userId всех пользователей, состоящих в данной организации.</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsByOrgAsync(string orgSlug, CancellationToken ct);
}
