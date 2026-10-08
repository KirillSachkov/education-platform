using AssignmentReviewService.Domain.Vcs;

namespace AssignmentReviewService.Core.Vcs.Models;

/// <summary>
///     Metadata об installation'е GitHub App, прочитанная у GitHub API после
///     успешной установки. Используется в Phase 4 install-callback для записи
///     <see cref="VcsInstallation"/>.
/// </summary>
public sealed record VcsInstallationDetail(
    string OwnerLogin,
    string OwnerExternalId,
    VcsInstallationOwnerType OwnerType,
    RepoSelections RepoSelections);
