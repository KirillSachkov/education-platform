namespace AssignmentReviewService.Contracts.Installations;

/// <summary>
///     Public-facing snapshot of a user's GitHub App installation. Returned by
///     <c>GET /assignment-review/installations/me</c> для отображения в
///     <c>/settings/integrations</c> на фронте — карточка «AI-проверка PR'ов».
/// </summary>
public sealed record VcsInstallationDto
{
    /// <summary>Internal aggregate id.</summary>
    public required Guid Id { get; init; }

    /// <summary>VCS provider name (currently always "GitHub").</summary>
    public required string Provider { get; init; }

    /// <summary>Owner of the GitHub account/org where App is installed.</summary>
    public required string OwnerLogin { get; init; }

    /// <summary>"User" or "Organization".</summary>
    public required string OwnerType { get; init; }

    /// <summary>"ACTIVE" / "SUSPENDED" / "UNINSTALLED".</summary>
    public required string Status { get; init; }

    /// <summary>True if user selected "All repositories" during App install.</summary>
    public required bool AllRepos { get; init; }

    /// <summary>
    ///     Whitelist of repo full names (<c>owner/repo</c>) selected during install.
    ///     Пуст если <see cref="AllRepos"/> = true.
    /// </summary>
    public required IReadOnlyList<string> Repos { get; init; }

    public required DateTimeOffset InstalledAt { get; init; }

    public DateTimeOffset? RemovedAt { get; init; }
}

public sealed record GetMyInstallationsResponse
{
    public required IReadOnlyList<VcsInstallationDto> Installations { get; init; }
}
