using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Billing;
using AccessService.Contracts.Plans.Requests;
using AccessService.Contracts.TrainerPro;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.TrainerPro;

/// <summary>
/// Covers the dedicated trainer-subscription surface (#674): public offer read, admin offer
/// create/update/list, and the scope-guarded purchase endpoint — plus the symmetric guard that
/// keeps trainer offers off the platform catalog/order paths.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class TrainerProOfferTests : AccessServiceTestsBase
{
    public TrainerProOfferTests(IntegrationTestsWebFactory factory) : base(factory) { }

    // ── Admin create ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_create_publishes_subscription_trainer_plan()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-monthly", priceCents: 49_000, intervalDays: 30);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.Equal(PlanTier.SUBSCRIPTION, plan.Tier);
            Assert.Equal(PlanOfferType.TRAINER_PRO, plan.OfferType);
            Assert.Equal(PlanScope.TRAINER, plan.Scope);
            Assert.True(plan.IsPublic);
            Assert.True(plan.IsActive);
            Assert.Equal(PlanTermKind.RECURRING, plan.Term.Kind);
            Assert.Equal(30, plan.Term.RecurringIntervalDays);
            Assert.Equal(49_000, plan.PriceCents);
            Assert.True(plan.Capabilities.HasFlag(PlanCapabilities.TRAINER_PRO));
        });
    }

    [Fact]
    public async Task Admin_create_inactive_does_not_publish_and_hidden_from_offer()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-draft", priceCents: 49_000, intervalDays: 30, isActive: false);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.False(plan.IsPublic);
            Assert.Equal(PlanScope.TRAINER, plan.Scope);
        });

        RemoveAuthentication();
        IReadOnlyList<TrainerProOfferDto> offer = await GetPublicOfferAsync();
        Assert.DoesNotContain(offer, o => o.Id == planId);
    }

    [Fact]
    public async Task Admin_create_rejects_non_positive_price()
    {
        AuthenticateAs("platform-author");
        CreateTrainerProOfferRequest request = new(
            Slug: "pro-bad-price", DisplayName: "Pro", PriceCents: 0, RecurringIntervalDays: 30);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/admin/trainer-pro/offer", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "trainer_pro.offer.price_invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_create_rejects_non_positive_interval()
    {
        AuthenticateAs("platform-author");
        CreateTrainerProOfferRequest request = new(
            Slug: "pro-bad-interval", DisplayName: "Pro", PriceCents: 49_000, RecurringIntervalDays: 0);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/admin/trainer-pro/offer", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "trainer_pro.offer.interval_invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_create_duplicate_slug_returns_conflict()
    {
        await CreateTrainerOfferAsync("pro-dup", priceCents: 49_000, intervalDays: 30);

        AuthenticateAs("platform-author");
        CreateTrainerProOfferRequest request = new(
            Slug: "pro-dup", DisplayName: "Pro", PriceCents: 49_000, RecurringIntervalDays: 30);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/admin/trainer-pro/offer", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_create_requires_plans_manage()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());
        CreateTrainerProOfferRequest request = new(
            Slug: "pro-forbidden", DisplayName: "Pro", PriceCents: 49_000, RecurringIntervalDays: 30);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/admin/trainer-pro/offer", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Admin list ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_get_offer_returns_published_and_draft_variants()
    {
        Guid activeId = await CreateTrainerOfferAsync("pro-active", priceCents: 49_000, intervalDays: 30);
        Guid draftId = await CreateTrainerOfferAsync("pro-hidden", priceCents: 490_000, intervalDays: 365, isActive: false);

        AuthenticateAs("platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/admin/trainer-pro/offer");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<TrainerProOfferAdminDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<TrainerProOfferAdminDto>>>();
        Assert.NotNull(envelope);
        Assert.Equal(2, envelope!.Result!.Count);

        TrainerProOfferAdminDto active = envelope.Result.Single(o => o.Id == activeId);
        TrainerProOfferAdminDto draft = envelope.Result.Single(o => o.Id == draftId);
        Assert.True(active.IsPublic);
        Assert.False(draft.IsPublic);
        Assert.Equal(30, active.RecurringIntervalDays);
        Assert.Equal(365, draft.RecurringIntervalDays);
    }

    [Fact]
    public async Task Admin_get_offer_requires_plans_manage()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/admin/trainer-pro/offer");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Admin update ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_update_changes_price()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-price", priceCents: 49_000, intervalDays: 30);

        AuthenticateAs("platform-author");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/admin/trainer-pro/offer/{planId}",
            new UpdateTrainerProOfferRequest(PriceCents: 59_000));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.Equal(59_000, plan.PriceCents);
        });
    }

    [Fact]
    public async Task Admin_update_active_false_unpublishes_and_removes_from_offer()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-toggle", priceCents: 49_000, intervalDays: 30);

        AuthenticateAs("platform-author");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/admin/trainer-pro/offer/{planId}",
            new UpdateTrainerProOfferRequest(IsActive: false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.False(plan.IsPublic);
        });

        RemoveAuthentication();
        IReadOnlyList<TrainerProOfferDto> offer = await GetPublicOfferAsync();
        Assert.DoesNotContain(offer, o => o.Id == planId);
    }

    [Fact]
    public async Task Admin_update_active_true_republishes()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-republish", priceCents: 49_000, intervalDays: 30, isActive: false);

        AuthenticateAs("platform-author");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/admin/trainer-pro/offer/{planId}",
            new UpdateTrainerProOfferRequest(IsActive: true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RemoveAuthentication();
        IReadOnlyList<TrainerProOfferDto> offer = await GetPublicOfferAsync();
        Assert.Contains(offer, o => o.Id == planId);
    }

    [Fact]
    public async Task Admin_update_rejects_platform_scoped_plan()
    {
        Guid platformPlanId = await CreatePublishedPlatformPlanAsync("platform-not-trainer", priceCents: 990_000);

        AuthenticateAs("platform-author");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/admin/trainer-pro/offer/{platformPlanId}",
            new UpdateTrainerProOfferRequest(PriceCents: 1_000));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "trainer_pro.offer.scope_mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_update_not_found_returns_404()
    {
        AuthenticateAs("platform-author");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/admin/trainer-pro/offer/{Guid.NewGuid()}",
            new UpdateTrainerProOfferRequest(PriceCents: 1_000));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_update_requires_plans_manage()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-update-forbidden", priceCents: 49_000, intervalDays: 30);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/admin/trainer-pro/offer/{planId}",
            new UpdateTrainerProOfferRequest(PriceCents: 1_000));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Public offer read ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Public_offer_returns_only_published_trainer_plans()
    {
        Guid trainerId = await CreateTrainerOfferAsync("pro-public", priceCents: 49_000, intervalDays: 30);
        await CreateTrainerOfferAsync("pro-hidden-2", priceCents: 99_000, intervalDays: 90, isActive: false);
        Guid platformPlanId = await CreatePublishedPlatformPlanAsync("platform-coexist", priceCents: 990_000);

        RemoveAuthentication();
        IReadOnlyList<TrainerProOfferDto> offer = await GetPublicOfferAsync();

        TrainerProOfferDto dto = Assert.Single(offer);
        Assert.Equal(trainerId, dto.Id);
        Assert.Equal(49_000, dto.PriceCents);
        Assert.Equal(49_000, dto.EffectivePriceCents);
        Assert.Equal(30, dto.RecurringIntervalDays);
        Assert.Contains("TRAINER_PRO", dto.Capabilities);
        Assert.DoesNotContain(offer, o => o.Id == platformPlanId);
    }

    [Fact]
    public async Task Public_offer_empty_when_no_trainer_plans()
    {
        await CreatePublishedPlatformPlanAsync("platform-only", priceCents: 990_000);

        RemoveAuthentication();
        IReadOnlyList<TrainerProOfferDto> offer = await GetPublicOfferAsync();
        Assert.Empty(offer);
    }

    [Fact]
    public async Task Public_offer_is_anonymous()
    {
        await CreateTrainerOfferAsync("pro-anon", priceCents: 49_000, intervalDays: 30);

        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/trainer-pro/offer");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Purchase (orders) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Trainer_order_happy_path_creates_pending_order()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-buy", priceCents: 49_000, intervalDays: 30);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/trainer-pro/orders", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<CreateOrderResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>();
        CreateOrderResponse body = envelope!.Result!;
        Assert.NotEqual(Guid.Empty, body.OrderId);

        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == body.OrderId);
            Assert.Equal(OrderStatus.PENDING, order.Status);
            Assert.Equal(planId, order.PlanId);
            Assert.Equal(49_000, order.AmountCents);
        });
    }

    [Fact]
    public async Task Trainer_order_rejects_platform_scoped_plan()
    {
        Guid platformPlanId = await CreatePublishedPlatformPlanAsync("platform-via-trainer", priceCents: 990_000);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/trainer-pro/orders", new CreateOrderRequest(platformPlanId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "order.plan.platform_only", StringComparison.Ordinal));

        await ExecuteInDbAsync(async db =>
            Assert.Equal(0, await db.Orders.CountAsync(o => o.PlanId == platformPlanId)));
    }

    [Fact]
    public async Task Trainer_order_unpublished_offer_returns_400()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-unpublished", priceCents: 49_000, intervalDays: 30, isActive: false);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/trainer-pro/orders", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Trainer_order_plan_not_found_returns_404()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/trainer-pro/orders", new CreateOrderRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Trainer_order_anonymous_returns_401()
    {
        Guid planId = await CreateTrainerOfferAsync("pro-anon-order", priceCents: 49_000, intervalDays: 30);

        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/trainer-pro/orders", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private async Task<Guid> CreateTrainerOfferAsync(
        string slug, int priceCents, int intervalDays, bool isActive = true)
    {
        AuthenticateAs("platform-author");
        CreateTrainerProOfferRequest request = new(
            Slug: slug,
            DisplayName: "Тренажёр Pro",
            PriceCents: priceCents,
            RecurringIntervalDays: intervalDays,
            ShortDescription: "Подписка на тренажёр собеседований",
            IsActive: isActive);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/admin/trainer-pro/offer", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;
    }

    private async Task<Guid> CreatePublishedPlatformPlanAsync(string slug, int priceCents)
    {
        AuthenticateAs("platform-author");
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
        Guid planId = (await create.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;

        HttpResponseMessage publish = await AppHttpClient.PostAsync($"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();
        return planId;
    }

    private async Task<IReadOnlyList<TrainerProOfferDto>> GetPublicOfferAsync()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/trainer-pro/offer");
        response.EnsureSuccessStatusCode();
        Envelope<IReadOnlyList<TrainerProOfferDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<TrainerProOfferDto>>>();
        return envelope!.Result!;
    }
}
