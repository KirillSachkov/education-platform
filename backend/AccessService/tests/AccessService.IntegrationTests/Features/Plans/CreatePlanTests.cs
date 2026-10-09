using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CreatePlanTests : AccessServiceTestsBase
{
    public CreatePlanTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Author_creates_lifetime_all_plan()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "full-access",
            DisplayName: "Полный доступ",
            ShortDescription: "Доступ ко всему",
            LongDescription: "**Маркдаун.**",
            CoverFileId: null,
            Features: new[] { "Все курсы", "Все материалы" },
            PriceCents: 990_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 1);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotEqual(Guid.Empty, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync();
            Assert.Equal(CurrentUserId, plan.AuthorId);
            Assert.Equal(PlanTier.FULL_ALL, plan.Tier);
            Assert.True(plan.IncludesFutureContent);
            Assert.Equal("full-access", plan.Slug.Value);
            // FULL_ALL tier forces FULL_ACCESS offer-type regardless of input (not supplied here).
            Assert.Equal(PlanOfferType.FULL_ACCESS, plan.OfferType);
        });
    }

    [Fact]
    public async Task Non_rub_currency_is_rejected_before_plan_is_persisted()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "usd-plan",
            DisplayName: "USD plan",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 10_000,
            Currency: "USD",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ExecuteInDbAsync(async db => Assert.Equal(0, await db.Plans.CountAsync()));
    }

    [Fact]
    public async Task Course_plan_persists_supplied_offer_type()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: "intensive-course",
            DisplayName: "Интенсив",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [Guid.NewGuid()],
            DisplayOrder: 0,
            OfferType: nameof(PlanOfferType.INTENSIVE));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync();
            Assert.Equal(PlanTier.COURSE, plan.Tier);
            Assert.Equal(PlanOfferType.INTENSIVE, plan.OfferType);
        });
    }

    [Fact]
    public async Task Full_all_tier_forces_full_access_offer_type_even_if_course_supplied()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "forced-full-access",
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0,
            OfferType: nameof(PlanOfferType.COURSE));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync();
            Assert.Equal(PlanOfferType.FULL_ACCESS, plan.OfferType);
        });
    }

    [Fact]
    public async Task Course_tier_with_full_access_offer_type_returns_validation_error()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: "course-full-access",
            DisplayName: "Курс",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [Guid.NewGuid()],
            DisplayOrder: 0,
            OfferType: nameof(PlanOfferType.FULL_ACCESS));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "plan.offer_type.invalid_for_tier", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Author_cannot_create_two_plans_with_same_slug()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: "dotnet",
            DisplayName: ".NET",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [Guid.NewGuid()],
            DisplayOrder: 0);

        HttpResponseMessage first = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        first.EnsureSuccessStatusCode();

        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        Envelope? envelope = await second.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "plan.slug.conflict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Courses_kind_with_null_courseid_returns_validation_error()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: "empty-courses",
            DisplayName: "Empty",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "plan.course_id.required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retired_subscription_plan_is_rejected_even_with_valid_recurring_interval()
    {
        // #614: SUBSCRIPTION plan threads RecurringIntervalDays into PlanTerm.Recurring.
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.SUBSCRIPTION),
            Slug: "trainer-pro",
            DisplayName: "Trainer Pro",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 99_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0,
            RecurringIntervalDays: 30);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages, m => m.Code == "plan.offer.retired");
        await ExecuteInDbAsync(async db => Assert.Empty(await db.Plans.ToListAsync()));
    }

    [Fact]
    public async Task Subscription_plan_without_recurring_interval_is_rejected()
    {
        // #614: domain requires a positive recurring interval for SUBSCRIPTION.
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.SUBSCRIPTION),
            Slug: "trainer-pro-no-interval",
            DisplayName: "Trainer Pro",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 99_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0,
            RecurringIntervalDays: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(
            envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.offer.retired", StringComparison.Ordinal));
    }
}