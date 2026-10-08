using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// L2-тесты: проверяем что endpoint'ы Publish / Unpublish / Archive / Update price
/// для COURSE-tier плана публикуют PlanCourseBound / PlanCourseUnbound через outbox.
/// Без flush'а в handler'е (или в TransactionManager) — OutboxCollector останется пустым.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PlanCourseOutboxTests : AccessServiceTestsBase
{
    public PlanCourseOutboxTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Publish_COURSE_plan_publishes_PlanCourseBound()
    {
        Guid courseId = Guid.NewGuid();
        Guid planId = await CreateCoursePlanAsync(courseId, priceCents: 1_500_00);

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanCourseBound published = OutboxCollector.OfType<PlanCourseBound>().Single();
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(CurrentUserId, published.AuthorId);
        Assert.Equal(courseId, published.CourseId);
        Assert.Equal(1_500_00, published.PriceCents);
        Assert.Equal("RUB", published.Currency);
        Assert.True(published.IsActive);
        Assert.True(published.IsPublic);
    }

    [Fact]
    public async Task Unpublish_active_public_COURSE_plan_publishes_PlanCourseUnbound()
    {
        Guid courseId = Guid.NewGuid();
        Guid planId = await CreateCoursePlanAsync(courseId);

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/unpublish", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanCourseUnbound published = OutboxCollector.OfType<PlanCourseUnbound>().Single();
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(courseId, published.CourseId);
    }

    [Fact]
    public async Task Archive_of_published_COURSE_plan_publishes_PlanCourseUnbound()
    {
        Guid courseId = Guid.NewGuid();
        Guid planId = await CreateCoursePlanAsync(courseId);

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanCourseUnbound published = OutboxCollector.OfType<PlanCourseUnbound>().Single();
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(courseId, published.CourseId);
    }

    [Fact]
    public async Task Archive_of_unpublished_COURSE_plan_does_not_publish_PlanCourseUnbound()
    {
        // Plan was never published — never appeared in the catalog. Archive should
        // not emit a cache-invalidation event because no consumer caches it.
        Guid courseId = Guid.NewGuid();
        Guid planId = await CreateCoursePlanAsync(courseId);

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Empty(OutboxCollector.OfType<PlanCourseUnbound>());
    }

    [Fact]
    public async Task UpdatePrice_on_published_COURSE_plan_publishes_PlanCourseBound_with_new_price()
    {
        Guid courseId = Guid.NewGuid();
        Guid planId = await CreateCoursePlanAsync(courseId, priceCents: 1_000_00);

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();

        OutboxCollector.Clear();

        UpdatePlanRequest update = new(
            DisplayName: null,
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 2_500_00,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: null);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanCourseBound published = OutboxCollector.OfType<PlanCourseBound>().Single();
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(courseId, published.CourseId);
        Assert.Equal(2_500_00, published.PriceCents);
    }

    [Fact]
    public async Task UpdatePrice_on_unpublished_COURSE_plan_does_not_publish_PlanCourseBound()
    {
        Guid courseId = Guid.NewGuid();
        Guid planId = await CreateCoursePlanAsync(courseId, priceCents: 1_000_00);

        OutboxCollector.Clear();

        UpdatePlanRequest update = new(
            DisplayName: null,
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 2_500_00,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: null);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Empty(OutboxCollector.OfType<PlanCourseBound>());
    }

    [Fact]
    public async Task Create_COURSE_plan_does_not_publish_PlanCourseBound()
    {
        OutboxCollector.Clear();

        Guid courseId = Guid.NewGuid();
        await CreateCoursePlanAsync(courseId);

        // Create-only path: plan is active but not public. No catalog visibility yet.
        Assert.Empty(OutboxCollector.OfType<PlanCourseBound>());
    }

    [Fact]
    public async Task Publish_COURSE_plan_without_price_publishes_PlanCourseBound_with_null_PriceCents()
    {
        // Plan created with PriceCents=null — Publish must propagate the null through
        // the integration event so consumers can distinguish "no price set" from 0 ₽.
        Guid courseId = Guid.NewGuid();
        Guid planId = await CreateCoursePlanAsync(courseId, slug: "outbox-no-price-plan", priceCents: null);

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanCourseBound published = OutboxCollector.OfType<PlanCourseBound>().Single();
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(courseId, published.CourseId);
        Assert.Null(published.PriceCents);
    }

    [Fact]
    public async Task Update_course_scope_and_capabilities_publishes_entitlements_changed()
    {
        Guid planId = await CreateCoursePlanAsync(Guid.NewGuid());
        OutboxCollector.Clear();

        UpdatePlanRequest update = new(
            DisplayName: null,
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: null,
            CourseIds: [Guid.NewGuid()],
            DisplayOrder: null,
            Capabilities: [nameof(PlanCapabilities.VIEW_MATERIALS)]);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPlanEntitlementsChanged(planId);
    }

    [Fact]
    public async Task Archive_and_unarchive_publish_entitlements_changed()
    {
        Guid planId = await CreateCoursePlanAsync(Guid.NewGuid());

        OutboxCollector.Clear();
        HttpResponseMessage archive = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        AssertPlanEntitlementsChanged(planId);

        OutboxCollector.Clear();
        HttpResponseMessage unarchive = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/unarchive", content: null);
        Assert.Equal(HttpStatusCode.OK, unarchive.StatusCode);
        AssertPlanEntitlementsChanged(planId);
    }

    private void AssertPlanEntitlementsChanged(Guid planId)
    {
        object message = Assert.Single(
            OutboxCollector.Messages,
            candidate => string.Equals(
                candidate.GetType().Name,
                "PlanEntitlementsChanged",
                StringComparison.Ordinal));
        object? actualPlanId = message.GetType().GetProperty("PlanId")?.GetValue(message);
        Assert.Equal(planId, Assert.IsType<Guid>(actualPlanId));
    }

    private async Task<Guid> CreateCoursePlanAsync(
        Guid courseId,
        string slug = "outbox-course-plan",
        long? priceCents = 1_000_00)
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: slug,
            DisplayName: "Курс План",
            ShortDescription: "Доступ к курсу",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
            Currency: "RUB",
            CourseIds: courseId is { } __cc ? [__cc] : [],
            DisplayOrder: 0);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        response.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        return envelope!.Result;
    }
}
