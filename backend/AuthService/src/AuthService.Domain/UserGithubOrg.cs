namespace AuthService.Domain;

/// <summary>
/// Кэш GitHub-организаций, в которых состоит пользователь. Заполняется в момент GitHub-логина/линка.
/// Используется для авто-зачисления на курсы по org-slug — фоновой задачей и реактивно при смене org у курса.
/// </summary>
public sealed record UserGithubOrg
{
    public required Guid UserId { get; init; }

    /// <summary>Lowercase slug GitHub-организации.</summary>
    public required string OrgSlug { get; init; }

    /// <summary>Время последней успешной синхронизации с GitHub API.</summary>
    public required DateTime SyncedAt { get; init; }
}
