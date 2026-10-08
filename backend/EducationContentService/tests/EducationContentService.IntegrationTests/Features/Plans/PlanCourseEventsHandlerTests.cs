using EducationContentService.Core.Features.Plans;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace EducationContentService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class PlanCourseEventsHandlerTests : EducationContentServiceTestsBase
{
    public PlanCourseEventsHandlerTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task PlanCourseBound_RemovesCacheEntryForCourse()
    {
        Guid courseId = Guid.NewGuid();
        HybridCache cache = Services.GetRequiredService<HybridCache>();
        CoursePricingDto pricing = MakePricing(courseId);

        await cache.SetAsync($"access:plan-for-course:{courseId}", pricing);
        Assert.True(await IsCachedAsync(cache, courseId));

        await InvokeMessageAndWaitAsync(new PlanCourseBound(
            PlanId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            CourseId: courseId,
            PriceCents: 100_00,
            Currency: "RUB",
            IsActive: true,
            IsPublic: true));

        Assert.False(await IsCachedAsync(cache, courseId));
    }

    [Fact]
    public async Task PlanCourseUnbound_RemovesCacheEntryForCourse()
    {
        Guid courseId = Guid.NewGuid();
        HybridCache cache = Services.GetRequiredService<HybridCache>();
        CoursePricingDto pricing = MakePricing(courseId);

        await cache.SetAsync($"access:plan-for-course:{courseId}", pricing);
        Assert.True(await IsCachedAsync(cache, courseId));

        await InvokeMessageAndWaitAsync(new PlanCourseUnbound(
            PlanId: Guid.NewGuid(),
            CourseId: courseId));

        Assert.False(await IsCachedAsync(cache, courseId));
    }

    private static async Task<bool> IsCachedAsync(HybridCache cache, Guid courseId)
    {
        bool factoryCalled = false;
        CoursePricingDto? cached = await cache.GetOrCreateAsync<CoursePricingDto?>(
            $"access:plan-for-course:{courseId}",
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult<CoursePricingDto?>(null);
            });

        // Clean up: if we just wrote the null placeholder via factory call,
        // remove it so subsequent assertions/tests are not polluted.
        if (factoryCalled)
            await cache.RemoveAsync($"access:plan-for-course:{courseId}");

        return !factoryCalled && cached is not null;
    }

    private static CoursePricingDto MakePricing(Guid courseId) =>
        new(
            PlanId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            CourseId: courseId,
            Slug: "plan-test",
            DisplayName: "Test Plan",
            PriceCents: 100_00,
            Currency: "RUB",
            DiscountPercent: null,
            DiscountStartsAt: null,
            DiscountEndsAt: null);
}
