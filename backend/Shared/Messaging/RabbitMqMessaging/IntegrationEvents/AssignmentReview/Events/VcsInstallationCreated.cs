namespace Shared.Messaging.IntegrationEvents.AssignmentReview;

/// <summary>
///     Опубликован AssignmentReviewService при успешном завершении install-flow
///     GitHub App (Phase 4, issue #15). Покрывает три кейса: fresh install,
///     re-install (тот же installation_id, обновлены метаданные / repo selections),
///     reactivate (был removed → стал ACTIVE).
///
///     Consumer (issue #307): AccessService — auto-complete GITHUB_REVIEW_APP
///     onboarding step для active <c>UserPlanOnboarding</c> юзера.
/// </summary>
public sealed record VcsInstallationCreated(
    Guid UserId,
    long InstallationId,
    string OwnerLogin,
    string OwnerType,
    DateTimeOffset OccurredAt);
