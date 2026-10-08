namespace AccessService.Contracts.PlanGrants.Requests;

/// <summary>
/// Internal request for granting a specific plan to a user. Used by service-to-service
/// callers (e.g., TelegramBotService F3 reverse claim flow).
/// Idempotent — same (user, plan, ACTIVE) returns existing grant.
/// </summary>
/// <param name="UserId">Recipient user ID.</param>
/// <param name="PlanId">Plan to grant.</param>
/// <param name="Source">
/// One of: <c>GITHUB_ORG</c>, <c>TELEGRAM_F1</c>, <c>ADMIN_GRANT</c>. Service callers
/// must pick the right source; AccessService validates the value.
/// </param>
/// <param name="SourceRef">Optional Guid reference (audit metadata).</param>
public sealed record GrantByPlanRequest(
    Guid UserId,
    Guid PlanId,
    string Source,
    Guid? SourceRef);
