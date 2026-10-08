using CSharpFunctionalExtensions;
using EducationContentService.Core.Features.Plans;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Unit.Plans;

/// <summary>
///     Unit tests for <see cref="CachedCoursePricingClient" />. Exercise the decorator
///     against a fresh, in-memory <see cref="HybridCache" /> (no Redis) per test, so each
///     case starts with a cold cache. Inner client is an NSubstitute mock.
/// </summary>
public sealed class CachedCoursePricingClientTests
{
    private static (CachedCoursePricingClient sut, ICoursePricingClient inner, ServiceProvider sp) BuildSut(
        ICoursePricingClient? innerOverride = null)
    {
        ICoursePricingClient inner = innerOverride ?? Substitute.For<ICoursePricingClient>();

        ServiceCollection services = new();
        services.AddMemoryCache();
        services.AddHybridCache();
        ServiceProvider sp = services.BuildServiceProvider();

        HybridCache cache = sp.GetRequiredService<HybridCache>();
        IOptions<AccessServiceOptions> options = Options.Create(new AccessServiceOptions
        {
            Url = "http://access-service:8010",
            CacheTtl = TimeSpan.FromMinutes(5),
        });

        CachedCoursePricingClient sut = new(
            inner,
            cache,
            options,
            NullLogger<CachedCoursePricingClient>.Instance);

        return (sut, inner, sp);
    }

    private static CoursePricingDto MakePricing(Guid courseId, long priceCents = 1_200_00) =>
        new(
            PlanId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            CourseId: courseId,
            Slug: "plan-" + courseId.ToString("N")[..8],
            DisplayName: "Course Plan",
            PriceCents: priceCents,
            Currency: "RUB",
            DiscountPercent: null,
            DiscountStartsAt: null,
            DiscountEndsAt: null);

    [Fact]
    public async Task EmptyInput_ReturnsEmpty_DoesNotCallInner()
    {
        (CachedCoursePricingClient sut, ICoursePricingClient inner, ServiceProvider sp) = BuildSut();
        using (sp)
        {
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> result =
                await sut.GetPlansForCoursesAsync([], CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
            await inner.DidNotReceive().GetPlansForCoursesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task CacheHit_SecondCall_DoesNotCallInner()
    {
        Guid courseId = Guid.NewGuid();
        CoursePricingDto pricing = MakePricing(courseId);

        ICoursePricingClient inner = Substitute.For<ICoursePricingClient>();
        inner.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(
                new Dictionary<Guid, CoursePricingDto> { [courseId] = pricing }));

        (CachedCoursePricingClient sut, _, ServiceProvider sp) = BuildSut(inner);
        using (sp)
        {
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> first =
                await sut.GetPlansForCoursesAsync([courseId], CancellationToken.None);
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> second =
                await sut.GetPlansForCoursesAsync([courseId], CancellationToken.None);

            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(pricing, first.Value[courseId]);
            Assert.Equal(pricing, second.Value[courseId]);

            await inner.Received(1).GetPlansForCoursesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task PartialMiss_FetchesOnlyMissing()
    {
        Guid cachedCourseId = Guid.NewGuid();
        Guid missingCourseId = Guid.NewGuid();
        CoursePricingDto cachedPricing = MakePricing(cachedCourseId, 1_000_00);
        CoursePricingDto missingPricing = MakePricing(missingCourseId, 2_000_00);

        ICoursePricingClient inner = Substitute.For<ICoursePricingClient>();
        inner.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(
                    new Dictionary<Guid, CoursePricingDto> { [cachedCourseId] = cachedPricing }),
                Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(
                    new Dictionary<Guid, CoursePricingDto> { [missingCourseId] = missingPricing }));

        (CachedCoursePricingClient sut, _, ServiceProvider sp) = BuildSut(inner);
        using (sp)
        {
            // Warm cache for cachedCourseId.
            await sut.GetPlansForCoursesAsync([cachedCourseId], CancellationToken.None);

            inner.ClearReceivedCalls();

            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> result =
                await sut.GetPlansForCoursesAsync(
                    [cachedCourseId, missingCourseId],
                    CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(cachedPricing, result.Value[cachedCourseId]);
            Assert.Equal(missingPricing, result.Value[missingCourseId]);

            // Inner called once, only with the missing id.
            await inner.Received(1).GetPlansForCoursesAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 1 && ids.Contains(missingCourseId)),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task NegativeCache_KnownMissingCourseId_DoesNotRefetch()
    {
        Guid noPlanCourseId = Guid.NewGuid();

        ICoursePricingClient inner = Substitute.For<ICoursePricingClient>();
        // Inner returns an empty dict — the course has no plan.
        inner.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(
                new Dictionary<Guid, CoursePricingDto>()));

        (CachedCoursePricingClient sut, _, ServiceProvider sp) = BuildSut(inner);
        using (sp)
        {
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> first =
                await sut.GetPlansForCoursesAsync([noPlanCourseId], CancellationToken.None);
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> second =
                await sut.GetPlansForCoursesAsync([noPlanCourseId], CancellationToken.None);

            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Empty(first.Value);
            Assert.Empty(second.Value);

            // Negative-cache sentinel keeps inner at exactly 1 call.
            await inner.Received(1).GetPlansForCoursesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task InnerThrowsHttpRequestException_ReturnsEmptySuccess_NoBubble()
    {
        Guid courseId = Guid.NewGuid();

        ICoursePricingClient inner = Substitute.For<ICoursePricingClient>();
        inner.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        (CachedCoursePricingClient sut, _, ServiceProvider sp) = BuildSut(inner);
        using (sp)
        {
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> result =
                await sut.GetPlansForCoursesAsync([courseId], CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }
    }

    [Fact]
    public async Task InnerThrowsTaskCanceled_ReturnsEmptySuccess_NoBubble()
    {
        Guid courseId = Guid.NewGuid();

        ICoursePricingClient inner = Substitute.For<ICoursePricingClient>();
        inner.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("inner client timeout"));

        (CachedCoursePricingClient sut, _, ServiceProvider sp) = BuildSut(inner);
        using (sp)
        {
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> result =
                await sut.GetPlansForCoursesAsync([courseId], CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }
    }

    [Fact]
    public async Task InnerReturnsFailure_SoftDegrades_ReturnsEmptySuccess()
    {
        Guid courseId = Guid.NewGuid();

        ICoursePricingClient inner = Substitute.For<ICoursePricingClient>();
        inner.GetPlansForCoursesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>(
                Error.Failure("service.unavailable", "AccessService is unavailable.").AsTransient()));

        (CachedCoursePricingClient sut, _, ServiceProvider sp) = BuildSut(inner);
        using (sp)
        {
            Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> result =
                await sut.GetPlansForCoursesAsync([courseId], CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }
    }
}
