namespace AssignmentReviewService.Domain.Vcs;

/// <summary>
///     Whitelist репозиториев на инсталляции GitHub App. Хранится как jsonb
///     в <see cref="VcsInstallation.RepoSelections"/>. <see cref="All"/> = "юзер
///     выбрал 'all repositories'" в GitHub App permission UI; в этом случае
///     <see cref="Repos"/> пустой.
/// </summary>
public sealed record RepoSelections
{
    public required bool All { get; init; }

    public required IReadOnlyList<string> Repos { get; init; }

    public static RepoSelections Empty() => new() { All = false, Repos = [] };

    public static RepoSelections AllRepos() => new() { All = true, Repos = [] };

    public static RepoSelections Specific(IReadOnlyList<string> repos) =>
        new() { All = false, Repos = repos };
}
