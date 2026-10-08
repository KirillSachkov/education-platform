namespace AssignmentReviewService.Domain.Vcs;

/// <summary>
///     Aggregate root: установка GitHub App юзером (или org'ом). Хранится owner
///     info + whitelist репозиториев + статус (ACTIVE / SUSPENDED / UNINSTALLED).
///     Никаких access-token'ов — installation-token выпускается on-demand
///     в Phase 3 через App private key.
/// </summary>
public sealed class VcsInstallation : AggregateRoot
{
    private VcsInstallation() { } // EF

    private VcsInstallation(
        VcsProvider provider,
        string installationId,
        VcsInstallationOwnerType ownerType,
        string ownerLogin,
        string ownerExternalId,
        Guid? linkedUserId,
        RepoSelections repoSelections,
        DateTimeOffset installedAt)
    {
        // Id = Guid.Empty — EF ValueGenerator (TimeOrderedGuidValueGenerator)
        // заполнит через Add. См. docs/agents/backend-transactions.md (#4).
        Id = Guid.Empty;
        Provider = provider;
        InstallationId = installationId;
        OwnerType = ownerType;
        // Owner login normalised to lowercase — domain invariant.
        // Lookup queries (RunIteration / awaiting-review handler)
        // лоуэркейсят входящий owner перед сравнением; storage обязан быть в том же
        // регистре, иначе Postgres case-sensitive WHERE никогда не сматчит
        // mixed-case org (Microsoft / OctoCat-Org → row "microsoft" vs query "Microsoft").
        // Зеркалит AccessService.AuthorGithubInstallation / GithubOrgInvitation pattern.
        OwnerLogin = ownerLogin?.ToLowerInvariant() ?? string.Empty;
        OwnerExternalId = ownerExternalId;
        LinkedUserId = linkedUserId;
        RepoSelections = repoSelections;
        Status = VcsInstallationStatus.ACTIVE;
        InstalledAt = installedAt;
    }

    public Guid Id { get; private set; }

    public VcsProvider Provider { get; private set; }

    public string InstallationId { get; private set; } = string.Empty;

    public VcsInstallationOwnerType OwnerType { get; private set; }

    public string OwnerLogin { get; private set; } = string.Empty;

    public string OwnerExternalId { get; private set; } = string.Empty;

    public Guid? LinkedUserId { get; private set; }

    public RepoSelections RepoSelections { get; private set; } = RepoSelections.Empty();

    public VcsInstallationStatus Status { get; private set; }

    public DateTimeOffset InstalledAt { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    /// <summary>
    ///     Создать installation. Записывается из install-callback'а (Phase 4) после
    ///     успешной валидации state-token'а и фетча detail у GitHub API.
    /// </summary>
    public static VcsInstallation Create(
        VcsProvider provider,
        string installationId,
        VcsInstallationOwnerType ownerType,
        string ownerLogin,
        string ownerExternalId,
        Guid? linkedUserId,
        RepoSelections repoSelections)
    {
        return new VcsInstallation(
            provider,
            installationId,
            ownerType,
            ownerLogin,
            ownerExternalId,
            linkedUserId,
            repoSelections,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     Привязать installation к нашему юзеру. Вызывается из callback'а после
    ///     успешной валидации state-token'а (state хранит UserId).
    /// </summary>
    public void LinkUser(Guid userId)
    {
        LinkedUserId = userId;
    }

    /// <summary>
    ///     Перевести в SUSPENDED — на webhook'е <c>installation/suspend</c>.
    ///     Idempotent (повторный вызов оставляет статус). Не возвращаемся
    ///     из UNINSTALLED (студент сначала должен переустановить).
    /// </summary>
    public void Suspend()
    {
        if (Status == VcsInstallationStatus.UNINSTALLED) return;
        Status = VcsInstallationStatus.SUSPENDED;
    }

    /// <summary>Снова ACTIVE на <c>unsuspend</c> webhook'е.</summary>
    public void Unsuspend()
    {
        if (Status == VcsInstallationStatus.UNINSTALLED) return;
        Status = VcsInstallationStatus.ACTIVE;
    }

    /// <summary>На <c>installation/deleted</c> webhook'е. Неvozvratnaya operation.</summary>
    public void MarkUninstalled(DateTimeOffset removedAt)
    {
        Status = VcsInstallationStatus.UNINSTALLED;
        RemovedAt = removedAt;
    }

    /// <summary>
    ///     На re-install (студент переустановил App с теми же InstallationId/Provider).
    ///     Сбрасывает Status в ACTIVE, очищает RemovedAt.
    /// </summary>
    public void Reactivate()
    {
        Status = VcsInstallationStatus.ACTIVE;
        RemovedAt = null;
    }

    /// <summary>
    ///     Обновить whitelist репозиториев — на <c>installation_repositories</c>
    ///     webhook (added / removed events) или ре-fetch'е из GitHub API.
    /// </summary>
    public void UpdateRepoSelections(RepoSelections selections)
    {
        RepoSelections = selections;
    }

    /// <summary>
    ///     Обновить metadata об owner'е (login может меняться при rename — редко,
    ///     но бывает). Используется в callback'е на reinstall для обновления стейла.
    /// </summary>
    public void UpdateOwnerMetadata(string ownerLogin, string ownerExternalId, VcsInstallationOwnerType ownerType)
    {
        // См. invariant в ctor: owner login лоуэркейзится для case-insensitive lookup'а.
        OwnerLogin = ownerLogin?.ToLowerInvariant() ?? string.Empty;
        OwnerExternalId = ownerExternalId;
        OwnerType = ownerType;
    }
}
