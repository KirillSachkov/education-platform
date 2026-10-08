using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.HomePins;
using AccessService.Core.Features.HomePins.UseCases;
using AccessService.Domain;
using AccessService.Domain.HomePins;
using AccessService.IntegrationTests.Infrastructure;
using ContentAccess;
using Microsoft.EntityFrameworkCore;
using Ordering;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.HomePins;

/// <summary>
///     Student-facing home-pins dashboard: <c>GET /access/me/home-pins/</c>. Merge of pins
///     across the caller's ACTIVE-plan grants, dedup by material, cap 12, ECS-enriched +
///     per-item lock state, non-published filtered, ECS-down soft-degrade. Epic #397.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMyHomePinsTests : AccessServiceTestsBase
{
    public GetMyHomePinsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Returns_enriched_pins_for_active_plan()
    {
        Guid authorId = DefaultUserId;
        Plan plan = await SeedPlanAsync(authorId, PlanTier.FULL_ALL);
        await SeedActiveGrantAsync(DefaultUserId, plan.Id);

        Guid materialId = Guid.NewGuid();
        await SeedPinAsync(plan.Id, materialId, note: "начни отсюда");
        Factory.EduClient.AddMaterialSummary(
            materialId, title: "Введение", kind: "VIDEO", thumbnailUrl: "https://cdn/x.jpg");
        Factory.EntitlementChecker.GrantAll();

        AuthenticateAs("platform-participant", DefaultUserId);
        IReadOnlyList<HomePinDto> pins = await GetMyPinsAsync();

        HomePinDto pin = Assert.Single(pins);
        Assert.Equal(materialId, pin.MaterialId);
        Assert.Equal("Введение", pin.Title);
        Assert.Equal("VIDEO", pin.Kind);
        Assert.Equal("https://cdn/x.jpg", pin.ThumbnailUrl);
        Assert.Equal("начни отсюда", pin.Note);
        Assert.Equal($"/knowledge-base/{materialId}", pin.Href);
        Assert.True(pin.IsAccessible);
        Assert.Null(pin.LockReason);
    }

    [Fact]
    public async Task Per_item_lock_state_reflects_entitlement()
    {
        Plan plan = await SeedPlanAsync(DefaultUserId, PlanTier.FULL_ALL);
        await SeedActiveGrantAsync(DefaultUserId, plan.Id);

        Guid granted = Guid.NewGuid();
        Guid denied = Guid.NewGuid();
        await SeedPinAsync(plan.Id, granted);
        await SeedPinAsync(plan.Id, denied);
        Factory.EduClient.AddMaterialSummary(granted, accessType: "PUBLIC");
        Factory.EduClient.AddMaterialSummary(denied, accessType: "ENROLLED");

        // Deny everything except the granted material.
        Factory.EntitlementChecker.DenyAll();
        Factory.EntitlementChecker.SetDecision(
            ResourceTypes.MATERIAL, granted, AccessDecision.Granted(AccessReason.ENTITLEMENT));

        AuthenticateAs("platform-participant", DefaultUserId);
        IReadOnlyList<HomePinDto> pins = await GetMyPinsAsync();

        HomePinDto grantedPin = Assert.Single(pins, p => p.MaterialId == granted);
        Assert.True(grantedPin.IsAccessible);
        Assert.Null(grantedPin.LockReason);

        HomePinDto deniedPin = Assert.Single(pins, p => p.MaterialId == denied);
        Assert.False(deniedPin.IsAccessible);
        Assert.Equal(LockReasons.PLAN_REQUIRED, deniedPin.LockReason);
    }

    [Fact]
    public async Task Same_material_pinned_in_two_plans_is_returned_once()
    {
        Plan planA = await SeedPlanAsync(DefaultUserId, PlanTier.FULL_ALL);
        Plan planB = await SeedPlanAsync(DefaultUserId, PlanTier.COURSE, courseId: Guid.NewGuid());
        await SeedActiveGrantAsync(DefaultUserId, planA.Id);
        await SeedActiveGrantAsync(DefaultUserId, planB.Id);

        Guid materialId = Guid.NewGuid();
        await SeedPinAsync(planA.Id, materialId);
        await SeedPinAsync(planB.Id, materialId);
        Factory.EduClient.AddMaterialSummary(materialId);
        Factory.EntitlementChecker.GrantAll();

        AuthenticateAs("platform-participant", DefaultUserId);
        IReadOnlyList<HomePinDto> pins = await GetMyPinsAsync();

        Assert.Single(pins);
        Assert.Equal(materialId, pins[0].MaterialId);
    }

    [Fact]
    public async Task Caps_at_twelve_pins()
    {
        Plan plan = await SeedPlanAsync(DefaultUserId, PlanTier.FULL_ALL);
        await SeedActiveGrantAsync(DefaultUserId, plan.Id);

        for (int i = 0; i < 15; i++)
        {
            Guid materialId = Guid.NewGuid();
            await SeedPinAsync(plan.Id, materialId);
            Factory.EduClient.AddMaterialSummary(materialId);
        }
        Factory.EntitlementChecker.GrantAll();

        AuthenticateAs("platform-participant", DefaultUserId);
        IReadOnlyList<HomePinDto> pins = await GetMyPinsAsync();

        Assert.Equal(GetMyHomePinsHandler.MAX_PINS, pins.Count);
    }

    [Fact]
    public async Task Non_published_material_is_filtered_out()
    {
        Plan plan = await SeedPlanAsync(DefaultUserId, PlanTier.FULL_ALL);
        await SeedActiveGrantAsync(DefaultUserId, plan.Id);

        Guid published = Guid.NewGuid();
        Guid draft = Guid.NewGuid();
        await SeedPinAsync(plan.Id, published);
        await SeedPinAsync(plan.Id, draft);
        Factory.EduClient.AddMaterialSummary(published, status: "PUBLISHED");
        Factory.EduClient.AddMaterialSummary(draft, status: "DRAFT");
        Factory.EntitlementChecker.GrantAll();

        AuthenticateAs("platform-participant", DefaultUserId);
        IReadOnlyList<HomePinDto> pins = await GetMyPinsAsync();

        Assert.Equal(published, Assert.Single(pins).MaterialId);
    }

    [Fact]
    public async Task Ecs_down_soft_degrades_to_empty_200()
    {
        Plan plan = await SeedPlanAsync(DefaultUserId, PlanTier.FULL_ALL);
        await SeedActiveGrantAsync(DefaultUserId, plan.Id);
        await SeedPinAsync(plan.Id, Guid.NewGuid());
        Factory.EduClient.MaterialSummariesFailure = Error.Failure("ecs.down", "ECS unavailable");

        AuthenticateAs("platform-participant", DefaultUserId);

        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/me/home-pins/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Envelope<IReadOnlyList<HomePinDto>>? env =
            await resp.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<HomePinDto>>>();
        Assert.Empty(env!.Result!);
    }

    [Fact]
    public async Task Unauthenticated_returns_401()
    {
        RemoveAuthentication();

        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/me/home-pins/");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task No_active_grants_returns_empty_list()
    {
        // A pin exists but the caller has no grant covering its plan.
        Plan plan = await SeedPlanAsync(DefaultUserId, PlanTier.FULL_ALL);
        await SeedPinAsync(plan.Id, Guid.NewGuid());

        AuthenticateAs("platform-participant", DefaultUserId);
        IReadOnlyList<HomePinDto> pins = await GetMyPinsAsync();

        Assert.Empty(pins);
    }

    [Fact]
    public async Task Revoked_grant_does_not_surface_pins()
    {
        Plan plan = await SeedPlanAsync(DefaultUserId, PlanTier.FULL_ALL);
        await SeedActiveGrantAsync(DefaultUserId, plan.Id, status: PlanGrantStatus.REVOKED);

        Guid materialId = Guid.NewGuid();
        await SeedPinAsync(plan.Id, materialId);
        Factory.EduClient.AddMaterialSummary(materialId);
        Factory.EntitlementChecker.GrantAll();

        AuthenticateAs("platform-participant", DefaultUserId);
        IReadOnlyList<HomePinDto> pins = await GetMyPinsAsync();

        Assert.Empty(pins);
    }

    // ───────────── Helpers ─────────────

    private async Task<Plan> SeedPlanAsync(Guid authorId, PlanTier tier, Guid? courseId = null)
    {
        Plan plan = Plan.Create(
            authorId: authorId,
            tier: tier,
            slug: PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Test plan").Value,
            courseIds: courseId is { } __cc ? [__cc] : [],
            requestedCapabilities: null).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });

        return plan;
    }

    private async Task SeedActiveGrantAsync(
        Guid userId, Guid planId, PlanGrantStatus status = PlanGrantStatus.ACTIVE)
    {
        PlanGrant grant = PlanGrant.Create(userId, planId, PlanGrantSource.ADMIN_GRANT, sourceRef: null);
        if (status == PlanGrantStatus.REVOKED)
            grant.Revoke(Guid.NewGuid(), "test");
        else if (status == PlanGrantStatus.EXPIRED)
            grant.Expire();

        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedPinAsync(Guid planId, Guid materialId, string? note = null)
    {
        await ExecuteInDbAsync(async db =>
        {
            // Append after the current last pin of the plan, mirroring AddHomePinHandler.
            // SortKey is value-converted to a string column → order in memory (the repo
            // does the same), not in SQL (EF can't translate p.SortKey.Value).
            List<PlanPinnedMaterial> existing = await db.PlanPinnedMaterials
                .Where(p => p.PlanId == planId)
                .ToListAsync();
            existing = existing.OrderBy(p => p.SortKey.Value, StringComparer.Ordinal).ToList();
            SortKey sortKey = existing.Count == 0
                ? SortKey.Initial()
                : SortKey.After(existing[^1].SortKey);

            PlanPinnedMaterial pin = PlanPinnedMaterial.Create(
                planId, materialId, note, sortKey, DateTimeOffset.UtcNow).Value;
            db.PlanPinnedMaterials.Add(pin);
            await db.SaveChangesAsync();
        });
    }

    private async Task<IReadOnlyList<HomePinDto>> GetMyPinsAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/me/home-pins/");
        resp.EnsureSuccessStatusCode();
        Envelope<IReadOnlyList<HomePinDto>>? env =
            await resp.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<HomePinDto>>>();
        return env!.Result!;
    }
}
