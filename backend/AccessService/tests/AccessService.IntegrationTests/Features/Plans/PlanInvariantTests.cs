using AccessService.Domain;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// Pure domain tests for the singular <see cref="Plan.CourseId"/> invariant.
/// COURSE tier requires non-null CourseId; all other tiers require null.
/// </summary>
public class PlanInvariantTests
{
    private static readonly PlanSlug Slug = PlanSlug.Of("test-plan").Value;
    private static readonly PlanDisplayName Name = PlanDisplayName.Of("Test").Value;

    [Fact]
    public void Create_CourseTier_WithoutCourseId_ReturnsFailure()
    {
        Result<Plan, Error> result = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.COURSE,
            slug: Slug,
            displayName: Name,
            courseIds: [],
            requestedCapabilities: null);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.course_id.required", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_FullAllTier_WithCourseId_ReturnsFailure()
    {
        Result<Plan, Error> result = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.FULL_ALL,
            slug: Slug,
            displayName: Name,
            courseIds: [Guid.NewGuid()],
            requestedCapabilities: null);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.course_id.forbidden", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_CourseTier_WithCourseId_Succeeds()
    {
        Guid courseId = Guid.NewGuid();

        Result<Plan, Error> result = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.COURSE,
            slug: Slug,
            displayName: Name,
            courseIds: courseId is { } __cc ? [__cc] : [],
            requestedCapabilities: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(courseId, result.Value.FirstCourseId);
    }

    [Fact]
    public void Create_FreeTier_Returns_DeprecatedError()
    {
        // Issue #358: FREE-tier creation deprecated at factory level.
        Result<Plan, Error> result = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.FREE,
            slug: Slug,
            displayName: Name,
            courseIds: [],
            requestedCapabilities: null);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.free.deprecated", result.Error.Messages[0].Code);
    }
}
