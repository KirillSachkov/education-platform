using AccessService.Domain;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
/// Guards the link between the <see cref="PlanTier"/> domain enum (whose <c>.ToString()</c> the
/// AccessService publisher emits into <see cref="PlanGrantCreated.PlanTier"/>) and the shared
/// wire-contract <see cref="PlanTierNames"/> constants that consumers (NotificationService,
/// TelegramBotService) key on. Consumer projects cannot reference the Domain enum, so if a future
/// rename/casing change drifts the two apart, this single test fails loudly instead of a consumer
/// branch silently no-op'ing (review #367, SubscribeOnPlanGrantCreatedHandler).
/// </summary>
public sealed class PlanTierNamesDriftTests
{
    [Fact]
    public void PlanTierNames_match_enum_member_names_exactly()
    {
        Assert.Equal(nameof(PlanTier.FREE), PlanTierNames.FREE);
        Assert.Equal(nameof(PlanTier.LEARN_ALL), PlanTierNames.LEARN_ALL);
        Assert.Equal(nameof(PlanTier.FULL_ALL), PlanTierNames.FULL_ALL);
        Assert.Equal(nameof(PlanTier.COURSE), PlanTierNames.COURSE);
        Assert.Equal(nameof(PlanTier.SUBSCRIPTION), PlanTierNames.SUBSCRIPTION);
    }
}
