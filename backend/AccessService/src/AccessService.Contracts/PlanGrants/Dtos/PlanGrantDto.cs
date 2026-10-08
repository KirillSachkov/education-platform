namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
/// PlanGrant read DTO — projection used by both user-facing and admin endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Optional enrichment fields:
/// </para>
/// <list type="bullet">
///   <item><see cref="UserEmail"/>, <see cref="UserDisplayName"/>, <see cref="UserUsername"/>,
///     <see cref="UserAvatarId"/> — populated by <c>ListPlanGrants</c> (admin/author view)
///     so the UI can show "кому выдан grant" with name+avatar instead of raw UUID.</item>
///   <item><see cref="Plan"/> — populated by <c>GetMyGrants</c> (user view) so the
///     <c>/settings/plans</c> page can render plan details (capabilities, slug, tier)
///     without a second roundtrip per grant.</item>
/// </list>
/// <para>
/// Fields are <c>null</c> on creation/revoke endpoints (Redeem, AdminGrant, RevokeGrant)
/// because the caller already has full context.
/// </para>
/// </remarks>
public sealed record PlanGrantDto(
    Guid Id,
    Guid UserId,
    Guid PlanId,
    string Source,
    Guid? SourceRef,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    string Status,
    DateTimeOffset? RevokedAt,
    string? RevokeReason,
    string? UserEmail = null,
    string? UserDisplayName = null,
    string? UserUsername = null,
    Guid? UserAvatarId = null,
    PlanSummaryDto? Plan = null,
    Guid? TelegramBindingPlanId = null,
    IReadOnlyList<string>? Capabilities = null,
    DateTimeOffset? NextChargeAt = null,
    int ChargeFailureCount = 0,
    DateTimeOffset? RenewalGraceEndsAt = null,
    DateTimeOffset? AutoRenewalCancelledAt = null,
    DateTimeOffset? AccessEndsAt = null);
