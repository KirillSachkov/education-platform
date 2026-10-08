namespace AccessService.Contracts.PlanGrants.Requests;

/// <summary>
/// Internal request for granting an author's default LIFETIME_ALL plan to a user.
/// Used by service-to-service callers (e.g., ProgressService GitHub auto-enrollment).
/// Idempotent — same (user, default plan, ACTIVE) returns existing grant.
/// </summary>
/// <param name="UserId">Recipient user ID.</param>
/// <param name="AuthorId">Author whose default plan should be granted.</param>
/// <param name="Source">
/// One of: <c>GITHUB_ORG</c>, <c>TELEGRAM_F1</c>, <c>ADMIN_GRANT</c>. Service callers
/// must pick the right source; AccessService validates the value.
/// </param>
/// <param name="SourceRef">Optional reference (e.g., GitHub org slug as Guid hash, or null).</param>
public sealed record GrantByAuthorRequest(
    Guid UserId,
    Guid AuthorId,
    string Source,
    Guid? SourceRef);
