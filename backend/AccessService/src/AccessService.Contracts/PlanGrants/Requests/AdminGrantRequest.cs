namespace AccessService.Contracts.PlanGrants.Requests;

/// <summary>
/// Request payload for <c>POST /access/grants/admin/</c>.
/// </summary>
public sealed record AdminGrantRequest(
    Guid UserId,
    Guid PlanId,
    DateTimeOffset? ExpiresAt);
