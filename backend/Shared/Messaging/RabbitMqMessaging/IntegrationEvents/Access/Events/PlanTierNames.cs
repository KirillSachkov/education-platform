namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Wire-contract string values for the <c>PlanTier</c> carried by access integration events
/// (<see cref="PlanGrantCreated"/>, <see cref="PlanGrantExpired"/> etc.). The publisher emits
/// <c>plan.Tier.ToString()</c>, so these mirror the <c>AccessService.Domain.PlanTier</c> enum
/// member names exactly. Consumers MUST compare against these constants — never bare literals —
/// so a future casing/rename drift fails at one place instead of silently no-op'ing a branch.
///
/// <para>The <c>AccessService.Domain</c> enum is not reachable from consumer projects
/// (NotificationService, TelegramBotService), and <c>AccessService.Contracts</c> does not
/// reference Domain either; this Shared/Messaging contract is the common surface. Drift between
/// these constants and the enum is regression-covered by an AccessService test.</para>
/// </summary>
public static class PlanTierNames
{
    public const string FREE = "FREE";
    public const string LEARN_ALL = "LEARN_ALL";
    public const string FULL_ALL = "FULL_ALL";
    public const string COURSE = "COURSE";
    public const string SUBSCRIPTION = "SUBSCRIPTION";
}
