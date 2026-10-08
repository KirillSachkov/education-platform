using AccessService.Core.Features.PlanGrants.IntegrationEvents;
using AccessService.Domain;
using ContentAccess;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
///     Деривация capability-тегов из (Plan, PlanGrant) — авто-PRO для полного доступа (#568).
///     Чистый unit-тест статического калькулятора, без контейнеров.
/// </summary>
public sealed class PlanGrantTagCalculatorTests
{
    private static readonly PlanDisplayName Name = PlanDisplayName.Of("Полный доступ").Value;

    [Fact]
    public void Full_platform_grant_gets_trainer_pro_only_when_flag_on()
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

        List<string> withPro =
            [.. PlanGrantTagCalculator.CalculateForGrant(grant, plan, fullPlatformGrantsTrainerPro: true)];
        List<string> withoutPro =
            [.. PlanGrantTagCalculator.CalculateForGrant(grant, plan, fullPlatformGrantsTrainerPro: false)];

        // FULL_ALL + флаг → cap:TRAINER_PRO; без флага — нет. plan:all присутствует всегда (full access).
        Assert.Contains(proTag, withPro);
        Assert.Contains(GrantTags.PlanAll(), withPro);
        Assert.DoesNotContain(proTag, withoutPro);
        Assert.Contains(GrantTags.PlanAll(), withoutPro);
    }

    [Fact]
    public void Course_grant_never_gets_trainer_pro_from_the_full_platform_flag()
    {
        Guid courseId = Guid.NewGuid();
        Plan plan = Plan.Create(
            Guid.NewGuid(),
            PlanTier.COURSE,
            PlanSlug.Of("one-course").Value,
            Name,
            courseIds: [courseId],
            requestedCapabilities: null).Value;
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(), plan.Id, PlanGrantSource.ADMIN_GRANT, sourceRef: null);

        List<string> tags =
            [.. PlanGrantTagCalculator.CalculateForGrant(grant, plan, fullPlatformGrantsTrainerPro: true)];

        // Авто-PRO — только за полный доступ к ПЛАТФОРМЕ (#568, решение владельца): курс PRO не даёт.
        Assert.DoesNotContain(GrantTags.Capability(nameof(PlanCapabilities.TRAINER_PRO)), tags);
        Assert.Contains(GrantTags.PlanCourse(courseId), tags);
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

        Assert.Empty(PlanGrantTagCalculator.CalculateForGrant(grant, plan, fullPlatformGrantsTrainerPro: true));
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
