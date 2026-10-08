namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService при revoke <c>PlanGrant</c> (admin revoke, refund,
/// expired auto-revoke). Consumer'ы должны снять соответствующий доступ.
/// </summary>
/// <param name="GrantId">ID отозванного PlanGrant.</param>
/// <param name="UserId">ID пользователя.</param>
/// <param name="PlanId">ID плана.</param>
/// <param name="Reason">Опциональная человекочитаемая причина (admin reason / "expired" / etc).</param>
/// <param name="RevokedAt">Время revoke (UTC).</param>
public sealed record PlanGrantRevoked(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    string? Reason,
    DateTimeOffset RevokedAt,
    Guid? CanonicalTelegramPlanId = null);
