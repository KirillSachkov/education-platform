using System.Net;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.Plans;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Courses;

/// <summary>
///     Verifies that <c>GET /courses/catalog</c> enriches each course DTO with a
///     <see cref="CoursePricingBlock"/> built from <see cref="ICoursePricingClient"/>.
///     The factory mocks <c>ICoursePricingClient</c> via NSubstitute — tests override
///     the per-call behaviour.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CatalogPricingTests : EducationContentServiceTestsBase
{
    public CatalogPricingTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Catalog_returns_pricing_for_courses_with_plans()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseWithPlan = await CreatePublishedCourseInDb("Course With Plan", ct);
        Guid courseNoPlan = await CreatePublishedCourseInDb("Course No Plan", ct);

        Guid planId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        ICoursePricingClient client = Services.GetRequiredService<ICoursePricingClient>();
        Dictionary<Guid, CoursePricingDto> pricingDict = new()
        {
            [courseWithPlan] = new CoursePricingDto(
                PlanId: planId,
                AuthorId: authorId,
                CourseId: courseWithPlan,
                Slug: "plan-1",
                DisplayName: "Plan 1",
                PriceCents: 100_00,
                Currency: "RUB",
                DiscountPercent: null,
                DiscountStartsAt: null,
                DiscountEndsAt: null),
        };
        client.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(pricingDict));

        // Unique search keeps this test's cache key isolated from sibling tests.
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=50&search=Plan", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CursorResponse<CourseCatalogDto> body =
            await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        CourseCatalogDto withPlan = body.Items.Single(c => c.Id == courseWithPlan);
        CourseCatalogDto noPlan = body.Items.Single(c => c.Id == courseNoPlan);

        Assert.NotNull(withPlan.Pricing);
        Assert.Equal(planId, withPlan.Pricing!.PlanId);
        Assert.Equal(100_00, withPlan.Pricing.PriceCents);
        Assert.Equal("RUB", withPlan.Pricing.Currency);
        Assert.Equal("plan-1", withPlan.Pricing.PlanSlug);
        // No promotion → effective == list, inactive, no discount metadata.
        Assert.False(withPlan.Pricing.PromotionActive);
        Assert.Equal(100_00, withPlan.Pricing.EffectivePriceCents);
        Assert.Null(withPlan.Pricing.DiscountPercent);
        Assert.Null(noPlan.Pricing);
    }

    [Fact]
    public async Task Catalog_active_promotion_shows_effective_price_and_badge()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseWithPromo = await CreatePublishedCourseInDb("Course Promo", ct);

        ICoursePricingClient client = Services.GetRequiredService<ICoursePricingClient>();
        Dictionary<Guid, CoursePricingDto> pricingDict = new()
        {
            [courseWithPromo] = new CoursePricingDto(
                PlanId: Guid.NewGuid(),
                AuthorId: Guid.NewGuid(),
                CourseId: courseWithPromo,
                Slug: "promo-plan",
                DisplayName: "Promo Plan",
                PriceCents: 100_00,
                Currency: "RUB",
                DiscountPercent: 30,
                DiscountStartsAt: DateTimeOffset.UtcNow.AddDays(-1),
                DiscountEndsAt: DateTimeOffset.UtcNow.AddDays(7)),
        };
        client.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(pricingDict));

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=50&search=Promo", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CursorResponse<CourseCatalogDto> body =
            await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        CourseCatalogDto item = body.Items.Single(c => c.Id == courseWithPromo);
        Assert.NotNull(item.Pricing);
        Assert.True(item.Pricing!.PromotionActive);
        Assert.Equal(100_00, item.Pricing.PriceCents); // list price preserved
        Assert.Equal(70_00, item.Pricing.EffectivePriceCents); // -30%
        Assert.Equal(30, item.Pricing.DiscountPercent);
        Assert.NotNull(item.Pricing.DiscountEndsAt);
    }

    [Fact]
    public async Task Catalog_returns_no_pricing_when_access_service_returns_failure()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreatePublishedCourseInDb("Course Failure", ct);

        ICoursePricingClient client = Services.GetRequiredService<ICoursePricingClient>();
        client.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(
                Error.Failure("access.service.unavailable", "AccessService offline")));

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=50&search=Failure", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CursorResponse<CourseCatalogDto> body =
            await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        CourseCatalogDto item = body.Items.Single(c => c.Id == courseId);
        Assert.Null(item.Pricing);
    }

    private async Task<Guid> CreatePublishedCourseInDb(string title, CancellationToken ct)
    {
        Guid courseId = Guid.Empty;
        string uniquePrefix = Guid.NewGuid().ToString("N")[..8];

        await ExecuteInDb(async db =>
        {
            var course = new Course(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create($"{title} description").Value,
                CourseSlug.Create($"slug-{uniquePrefix}").Value,
                SortKey.Initial());
            course.Publish();
            db.Courses.Add(course);
            courseId = course.Id;
            await db.SaveChangesAsync(ct);
        });

        return courseId;
    }
}
