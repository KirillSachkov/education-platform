namespace AccessService.Domain.Integrations.GitHub;

/// <summary>
///     Запись об установке нашего GitHub App в org конкретного автора.
///     Один автор может ставить App в одну org одновременно (PK = author_id).
///     Хранит installation_id для генерации installation-token'ов GitHub API.
/// </summary>
public sealed class AuthorGithubInstallation
{
    private AuthorGithubInstallation() { } // EF

    private AuthorGithubInstallation(
        Guid authorId,
        long installationId,
        string orgLogin,
        DateTimeOffset installedAt)
    {
        AuthorId = authorId;
        InstallationId = installationId;
        OrgLogin = orgLogin;
        InstalledAt = installedAt;
    }

    public Guid AuthorId { get; private set; }

    public long InstallationId { get; private set; }

    public string OrgLogin { get; private set; } = null!;

    public DateTimeOffset InstalledAt { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public static AuthorGithubInstallation Create(
        Guid authorId, long installationId, string orgLogin, DateTimeOffset now) =>
        new(authorId, installationId, orgLogin.ToLowerInvariant(), now);

    /// <summary>
    ///     Reinstall: автор поставил App в другую org либо переустановил в ту же.
    ///     Сбрасывает suspended_at и обновляет installation_id + org_login.
    /// </summary>
    public void Reinstall(long installationId, string orgLogin, DateTimeOffset now)
    {
        InstallationId = installationId;
        OrgLogin = orgLogin.ToLowerInvariant();
        InstalledAt = now;
        SuspendedAt = null;
    }

    public void Suspend(DateTimeOffset now)
    {
        if (SuspendedAt.HasValue) return;
        SuspendedAt = now;
    }

    public void Unsuspend()
    {
        SuspendedAt = null;
    }

    public bool IsActive => !SuspendedAt.HasValue;
}
