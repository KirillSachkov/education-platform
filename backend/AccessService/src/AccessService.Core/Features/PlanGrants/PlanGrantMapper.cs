using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Domain;
using AuthService.Contracts;

namespace AccessService.Core.Features.PlanGrants;

internal static class PlanGrantMapper
{
    public static PlanGrantDto MapToDto(PlanGrant grant) => new(
        grant.Id,
        grant.UserId,
        grant.PlanId,
        grant.Source.ToString(),
        grant.SourceRef,
        grant.GrantedAt,
        grant.ExpiresAt,
        grant.Status.ToString(),
        grant.RevokedAt,
        grant.RevokeReason,
        NextChargeAt: grant.NextChargeAt,
        ChargeFailureCount: grant.ChargeFailureCount,
        RenewalGraceEndsAt: grant.RenewalGraceEndsAt,
        AutoRenewalCancelledAt: grant.AutoRenewalCancelledAt,
        AccessEndsAt: grant.AccessEndsAt);

    /// <summary>
    /// Admin-side view: enriches grant with the recipient user's email/name/avatar
    /// so the GrantsTab can render a human-readable row instead of a UUID.
    /// </summary>
    public static PlanGrantDto MapToDtoWithUser(PlanGrant grant, AuthUserLookupDto? user) => new(
        grant.Id,
        grant.UserId,
        grant.PlanId,
        grant.Source.ToString(),
        grant.SourceRef,
        grant.GrantedAt,
        grant.ExpiresAt,
        grant.Status.ToString(),
        grant.RevokedAt,
        grant.RevokeReason,
        UserEmail: user?.Email,
        UserDisplayName: user?.Name,
        UserUsername: user?.Username,
        UserAvatarId: user?.AvatarId,
        NextChargeAt: grant.NextChargeAt,
        ChargeFailureCount: grant.ChargeFailureCount,
        RenewalGraceEndsAt: grant.RenewalGraceEndsAt,
        AutoRenewalCancelledAt: grant.AutoRenewalCancelledAt,
        AccessEndsAt: grant.AccessEndsAt);

    /// <summary>
    /// User-side view: enriches grant with a lean Plan summary so /settings/plans
    /// can render plan card details in one round-trip. <paramref name="onboardingEnabledByPlanId"/>
    /// — словарь planId → IsEnabled из <see cref="Domain.Onboarding.PlanOnboardingFlow"/>;
    /// если ключа нет, считаем что flow не настроен (false).
    /// </summary>
    public static PlanGrantDto MapToDtoWithPlan(
        PlanGrant grant,
        Plan? plan,
        IReadOnlyDictionary<Guid, bool>? onboardingEnabledByPlanId = null) => new(
        grant.Id,
        grant.UserId,
        grant.PlanId,
        grant.Source.ToString(),
        grant.SourceRef,
        grant.GrantedAt,
        grant.ExpiresAt,
        grant.Status.ToString(),
        grant.RevokedAt,
        grant.RevokeReason,
        Plan: plan is null ? null : MapToPlanSummary(plan, GetOnboardingEnabled(plan.Id, onboardingEnabledByPlanId)),
        NextChargeAt: grant.NextChargeAt,
        ChargeFailureCount: grant.ChargeFailureCount,
        RenewalGraceEndsAt: grant.RenewalGraceEndsAt,
        AutoRenewalCancelledAt: grant.AutoRenewalCancelledAt,
        AccessEndsAt: grant.AccessEndsAt);

    public static PlanGrantDto MapToDtoWithTelegramAccess(
        PlanGrant grant,
        Plan? plan,
        Guid? telegramBindingPlanId) => new(
        grant.Id,
        grant.UserId,
        grant.PlanId,
        grant.Source.ToString(),
        grant.SourceRef,
        grant.GrantedAt,
        grant.ExpiresAt,
        grant.Status.ToString(),
        grant.RevokedAt,
        grant.RevokeReason,
        TelegramBindingPlanId: telegramBindingPlanId,
        Capabilities: plan is null ? null : PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
        NextChargeAt: grant.NextChargeAt,
        ChargeFailureCount: grant.ChargeFailureCount,
        RenewalGraceEndsAt: grant.RenewalGraceEndsAt,
        AutoRenewalCancelledAt: grant.AutoRenewalCancelledAt,
        AccessEndsAt: grant.AccessEndsAt);

    private static bool GetOnboardingEnabled(
        Guid planId,
        IReadOnlyDictionary<Guid, bool>? map) =>
        map is not null && map.TryGetValue(planId, out bool enabled) && enabled;

    private static PlanSummaryDto MapToPlanSummary(Plan plan, bool hasOnboardingEnabled) => new(
        plan.Id,
        plan.AuthorId,
        plan.Slug.Value,
        plan.DisplayName.Value,
        string.IsNullOrWhiteSpace(plan.ShortDescription) ? null : plan.ShortDescription,
        plan.Tier.ToString(),
        plan.CoverFileId,
        PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
        plan.GetCourseIds(),
        plan.IncludesFutureContent,
        plan.TrialDurationDays,
        hasOnboardingEnabled);
}
