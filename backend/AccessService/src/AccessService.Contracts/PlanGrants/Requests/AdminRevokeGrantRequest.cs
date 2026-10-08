namespace AccessService.Contracts.PlanGrants.Requests;

/// <summary>
/// Body для <c>POST /access/admin/grants/{grantId}/revoke</c> (#414). Reason обязателен —
/// пишется в <c>plan_grants.revoke_reason</c> и в лог для аудита ручного admin-отзыва.
/// </summary>
public sealed record AdminRevokeGrantRequest(string Reason);
