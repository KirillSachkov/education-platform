using AccessService.Core.Features.PlanGrants.Services;
using AccessService.Domain;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
/// Pure domain unit-tests на subset-rule из <see cref="UpgradeCreditCalculator"/>.
/// Покрывают 6 кейсов из design doc (2026-05-08) — без БД, без HTTP, только логика.
/// Phase 2 #112.
/// </summary>
public class UpgradeCreditCalculatorTests
{
    private static Plan MakePlan(
        PlanTier tier,
        Guid authorId,
        Guid? courseId = null,
        PlanCapabilities? caps = null,
        string? slug = null)
    {
        Result<Plan, Error> result = Plan.Create(
            authorId,
            tier,
            PlanSlug.Of(slug ?? $"plan-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Plan").Value,courseId is { } __cc ? [__cc] : [],
            requestedCapabilities: caps is null ? null : PlanCapabilitiesMapper.ToStrings(caps.Value));
        return result.Value;
    }

    [Fact]
    public void FULL_ALL_covers_COURSE_grant()
    {
        Guid authorId = Guid.NewGuid();
        Plan course = MakePlan(PlanTier.COURSE, authorId, courseId: Guid.NewGuid());
        Plan fullAll = MakePlan(PlanTier.FULL_ALL, authorId);

        Assert.True(UpgradeCreditCalculator.IsScopeSubset(course, fullAll));
    }

    [Fact]
    public void FULL_ALL_covers_cross_author_COURSE_grant()
    {
        Plan course = MakePlan(PlanTier.COURSE, Guid.NewGuid(), courseId: Guid.NewGuid());
        Plan fullAll = MakePlan(PlanTier.FULL_ALL, Guid.NewGuid());

        Assert.True(UpgradeCreditCalculator.IsScopeSubset(course, fullAll));
    }

    // Issue #358: PlanTier.FREE creation blocked at factory level (mirrors LEARN_ALL).
    // Tests FULL_ALL_covers_FREE_grant / COURSE_covers_FREE_grant / FREE_target_does_not_cover_anything
    // удалены — MakePlan(FREE) больше не получится, и реальный upgrade-flow не выпускает
    // FREE plans. Legacy FREE grants в БД покрываются default-arm switch'а (covered by
    // SUBSCRIPTION test когда Phase 3 unlock).

    [Fact]
    public void COURSE_covers_same_course_with_subset_capabilities()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        Plan grantedCourse = MakePlan(
            PlanTier.COURSE,
            authorId,
            courseId: courseId,
            caps: PlanCapabilities.VIEW_MATERIALS,
            slug: "granted");
        Plan targetCourse = MakePlan(
            PlanTier.COURSE,
            authorId,
            courseId: courseId,
            caps: PlanCapabilities.VIEW_MATERIALS | PlanCapabilities.SUBMIT_ISSUES,
            slug: "target");

        Assert.True(UpgradeCreditCalculator.IsScopeSubset(grantedCourse, targetCourse));
    }

    [Fact]
    public void COURSE_does_not_cover_disjoint_COURSE_grant()
    {
        Guid authorId = Guid.NewGuid();
        Plan courseA = MakePlan(PlanTier.COURSE, authorId, courseId: Guid.NewGuid());
        Plan courseB = MakePlan(PlanTier.COURSE, authorId, courseId: Guid.NewGuid());

        Assert.False(UpgradeCreditCalculator.IsScopeSubset(courseA, courseB));
    }

    [Fact]
    public void Self_upgrade_returns_false()
    {
        Guid authorId = Guid.NewGuid();
        Plan plan = MakePlan(PlanTier.FULL_ALL, authorId);

        Assert.False(UpgradeCreditCalculator.IsScopeSubset(plan, plan));
    }

    // ── Bundle (#404) — set-based COURSE subset ────────────────────────────────

    private static Plan MakeBundle(
        Guid authorId,
        IReadOnlyList<Guid> courseIds,
        PlanCapabilities? caps = null,
        string? slug = null)
    {
        Result<Plan, Error> result = Plan.Create(
            authorId,
            PlanTier.COURSE,
            PlanSlug.Of(slug ?? $"bundle-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Bundle").Value,
            courseIds,
            requestedCapabilities: caps is null ? null : PlanCapabilitiesMapper.ToStrings(caps.Value));
        return result.Value;
    }

    [Fact]
    public void Bundle_covers_single_course_subset() // owner's case: own {A} → buy {A,B} ⇒ credit
    {
        Guid authorId = Guid.NewGuid();
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Plan single = MakeBundle(authorId, [a], slug: "single");
        Plan bundle = MakeBundle(authorId, [a, b], slug: "bundle");

        Assert.True(UpgradeCreditCalculator.IsScopeSubset(single, bundle));
    }

    [Fact]
    public void Bundle_does_not_cover_grant_with_an_extra_course() // {A,B} ⊉ {A,C}
    {
        Guid authorId = Guid.NewGuid();
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        Plan granted = MakeBundle(authorId, [a, c], slug: "granted");
        Plan target = MakeBundle(authorId, [a, b], slug: "target");

        Assert.False(UpgradeCreditCalculator.IsScopeSubset(granted, target));
    }

    [Fact]
    public void Single_course_target_does_not_credit_a_bundle_grant() // {A} ⊉ {A,B}: no downgrade credit
    {
        Guid authorId = Guid.NewGuid();
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Plan grantedBundle = MakeBundle(authorId, [a, b], slug: "granted");
        Plan single = MakeBundle(authorId, [a], slug: "target");

        Assert.False(UpgradeCreditCalculator.IsScopeSubset(grantedBundle, single));
    }

    [Fact]
    public void Bundle_requires_capabilities_subset() // courses fit, but granted caps ⊄ target caps
    {
        Guid authorId = Guid.NewGuid();
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Plan granted = MakeBundle(
            authorId,
            [a],
            caps: PlanCapabilities.VIEW_MATERIALS | PlanCapabilities.CODE_REVIEW,
            slug: "granted");
        Plan target = MakeBundle(
            authorId,
            [a, b],
            caps: PlanCapabilities.VIEW_MATERIALS,
            slug: "target");

        Assert.False(UpgradeCreditCalculator.IsScopeSubset(granted, target));
    }

    // SUBSCRIPTION-tier и FREE-tier targets — Plan.Create блокирует оба (Phase 3 / #358).
    // Дефолтная ветка switch'а в IsScopeSubset (default-arm `_ => false`) не покрыта
    // explicit test'ом — instantiate Plan(SUBSCRIPTION|FREE) без reflection невозможно.
    // Когда Phase 3 unlock'нет SUBSCRIPTION — добавить explicit test.
}
