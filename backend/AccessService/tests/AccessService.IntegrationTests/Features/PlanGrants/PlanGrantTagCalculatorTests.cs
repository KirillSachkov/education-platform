using AccessService.Core.Features.PlanGrants.IntegrationEvents;
using AccessService.Domain;
using ContentAccess;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>Entitlement projection preserves platform access while retiring trainer grants.</summary>
public sealed class PlanGrantTagCalculatorTests
{
    private static readonly PlanDisplayName Name = PlanDisplayName.Of("Полный доступ").Value;

    [Fact]
    public void Full_platform_grant_keeps_platform_capabilities_without_retired_tag()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(),
            PlanTier.FULL_ALL,
            PlanSlug.Of("full-platform").Value,
            Name,
            courseIds: [],
            requestedCapabilities: null).Value;
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), plan.Id, PlanGrantSource.ADMIN_GRANT, sourceRef: null);

        string proTag = GrantTags.Capability(nameof(PlanCapabilities.TRAINER_PRO));

        List<string> tags = [.. PlanGrantTagCalculator.CalculateForGrant(grant, plan)];

        Assert.DoesNotContain(proTag, tags);
        Assert.Contains(GrantTags.PlanAll(), tags);
        foreach (string name in PlanCapabilitiesMapper.ToStrings(PlanCapabilities.FULL))
            Assert.Contains(GrantTags.Capability(name), tags);
    }

    [Fact]
    public void Course_grant_preserves_course_access_and_masks_persisted_retired_flag()
    {
        Guid courseId = Guid.NewGuid();
        Plan plan = Plan.Create(
            Guid.NewGuid(),
            PlanTier.COURSE,
            PlanSlug.Of("one-course").Value,
            Name,
            courseIds: [courseId],
            requestedCapabilities: ["VIEW_MATERIALS", "TRAINER_PRO"]).Value;
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), plan.Id, PlanGrantSource.ADMIN_GRANT, sourceRef: null);

        List<string> tags =
            [.. PlanGrantTagCalculator.CalculateForGrant(grant, plan)];

        Assert.DoesNotContain(GrantTags.Capability(nameof(PlanCapabilities.TRAINER_PRO)), tags);
        Assert.Contains(GrantTags.PlanCourse(courseId), tags);
        Assert.Contains(GrantTags.Capability(nameof(PlanCapabilities.VIEW_MATERIALS)), tags);
    }

    [Fact]
    public void Legacy_trainer_plan_yields_no_platform_or_capability_tags()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(), PlanTier.SUBSCRIPTION, PlanSlug.Of("legacy-trainer").Value,
            Name, [], ["TRAINER_PRO", "VIEW_MATERIALS"], term: PlanTerm.Recurring(30)).Value;
        PlanGrant grant = PlanGrant.Create(Guid.NewGuid(), plan.Id, PlanGrantSource.ADMIN_GRANT, null);

        Assert.Empty(PlanGrantTagCalculator.CalculateForGrant(grant, plan));
        Assert.Empty(PlanGrantTagCalculator.CalculateUnion([grant], new Dictionary<Guid, Plan> { [plan.Id] = plan }));
    }

    [Fact]
    public void Revoked_grant_yields_no_tags()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(),
            PlanTier.FULL_ALL,
            PlanSlug.Of("revoked-full").Value,
            Name,
            courseIds: [],
            requestedCapabilities: null).Value;
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), plan.Id, PlanGrantSource.ADMIN_GRANT, sourceRef: null);
        grant.Revoke(Guid.NewGuid(), "test");

        Assert.Empty(PlanGrantTagCalculator.CalculateForGrant(grant, plan));
    }

    [Fact]
    public void Active_grant_on_archived_plan_yields_no_tags()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(),
            PlanTier.FULL_ALL,
            PlanSlug.Of("archived-full").Value,
            Name,
            courseIds: [],
            requestedCapabilities: null).Value;
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), plan.Id, PlanGrantSource.ADMIN_GRANT, sourceRef: null);
        plan.Archive();

        Assert.Empty(PlanGrantTagCalculator.CalculateForGrant(grant, plan));
    }
}