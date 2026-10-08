using ContentAccess;
using EducationContentService.Core.Features.ContentAccess;
using EducationContentService.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace EducationContentService.IntegrationTests.Unit;

/// <summary>
///     Unit-тесты для <see cref="ContentAccessTagBuilder" /> — чистая функция,
///     не требует DB/Redis. Покрывает логику семантики AccessType → Redis-теги
    ///     после Phase E cutover (plan-tags only, no legacy course-tags/lifetime-tags), #599
    ///     (FULL_ALL/LEARN_ALL = global plan:all), #608
///     (orphan-материалы гейтятся через plan:all вместо sentinel) и #358
///     (legacy FREE удалён, бесплатный доступ = REGISTERED system default).
/// </summary>
public class ContentAccessTagBuilderTests
{
    private static readonly Guid CourseAId = Guid.Parse("019d0000-0000-7000-8000-000000000a00");
    private static readonly Guid CourseBId = Guid.Parse("019d0000-0000-7000-8000-000000000b00");
    private static readonly Guid ResourceId = Guid.Parse("019d0000-0000-7000-8000-000000000001");

    [Fact]
    public void Build_Public_ReturnsPublicTag()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            AccessType.PUBLIC, ResourceId, [CourseAId], NullLogger.Instance);

        Assert.Single(tags);
        Assert.Equal(GrantTags.PUBLIC, tags[0]);
    }

    [Fact]
    public void Build_Public_WithNoCourses_ReturnsPublicTag()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            AccessType.PUBLIC, ResourceId, [], NullLogger.Instance);

        Assert.Single(tags);
        Assert.Equal(GrantTags.PUBLIC, tags[0]);
    }

    [Fact]
    public void Build_Registered_ReturnsAuthenticatedTag()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            AccessType.REGISTERED, ResourceId, [CourseAId], NullLogger.Instance);

        Assert.Single(tags);
        Assert.Equal(GrantTags.AUTHENTICATED, tags[0]);
    }

    [Fact]
    public void Build_Enrolled_WithCourse_EmitsPlanAllAndPlanCourseOnly()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            AccessType.ENROLLED, ResourceId, [CourseAId], NullLogger.Instance);

        Assert.Equal(2, tags.Count);
        Assert.Contains(GrantTags.PlanAll(), tags);
        Assert.Contains($"plan:course:{CourseAId:D}", tags);
        Assert.DoesNotContain(tags, t => t.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
        Assert.DoesNotContain(tags, t => t.StartsWith("course:", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Enrolled_MultipleCoursesSameAuthor_EmitsOnePlanCoursePerCourse()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            AccessType.ENROLLED, ResourceId, [CourseAId, CourseBId], NullLogger.Instance);

        Assert.Equal(1, tags.Count(t => t == GrantTags.PlanAll()));
        Assert.Contains($"plan:course:{CourseAId:D}", tags);
        Assert.Contains($"plan:course:{CourseBId:D}", tags);
        Assert.DoesNotContain(tags, t => t.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Enrolled_DuplicateCourseIds_EmitsOnePlanCourseTag()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            AccessType.ENROLLED, ResourceId, [CourseAId, CourseAId], NullLogger.Instance);

        Assert.Contains(GrantTags.PlanAll(), tags);
        Assert.Contains($"plan:course:{CourseAId:D}", tags);
        Assert.Equal(1, tags.Count(t => t == GrantTags.PlanCourse(CourseAId)));
        Assert.DoesNotContain(tags, t => t.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

    // ---------- #77 orphan кейсы (plan-bound) ----------

    [Fact]
    public void Build_OrphanEnrolled_EmitsPlanAll()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            AccessType.ENROLLED, ResourceId, [], NullLogger.Instance);

        Assert.Single(tags);
        Assert.Equal(GrantTags.PlanAll(), tags[0]);
        Assert.DoesNotContain(tags, t => t.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_StringOverload_UnknownType_ReturnsSentinel()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            "SOMETHING_NEW", ResourceId, [CourseAId], NullLogger.Instance);

        Assert.Single(tags);
        Assert.Equal(GrantTags.ENROLLED_NO_COURSE, tags[0]);
    }

    [Fact]
    public void Build_StringOverload_Public_Lowercase_ReturnsPublicTag()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            "public", ResourceId, [CourseAId], NullLogger.Instance);

        Assert.Single(tags);
        Assert.Equal(GrantTags.PUBLIC, tags[0]);
    }

    [Fact]
    public void Build_StringOverload_Registered_MixedCase_ReturnsAuthenticatedTag()
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            "Registered", ResourceId, [CourseAId], NullLogger.Instance);

        Assert.Single(tags);
        Assert.Equal(GrantTags.AUTHENTICATED, tags[0]);
    }

    [Fact]
    public void Build_StringOverload_LegacyFree_CollapsesToAuthenticated()
    {
        // Issue #358: legacy "FREE" коллапсируется в REGISTERED (AUTHENTICATED tag).
        // Это путь in-flight event'ов с FREE до data-миграции.
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            "FREE", ResourceId, [CourseAId], NullLogger.Instance);

        Assert.Single(tags);
        Assert.Equal(GrantTags.AUTHENTICATED, tags[0]);
    }

    // ---------- #365 course-level resource gate ----------

    [Fact]
    public void BuildCourseAccessTags_EmitsLegacyCoursePlanAllAndPlanCourse()
    {
        // Курсовой гейт (resource-access:course:{id}) должен пускать автора (legacy
        // course:{id}), полный доступ (plan:all), курсовой план (plan:course:{id})
        IReadOnlyList<string> tags = ContentAccessTagBuilder.BuildCourseAccessTags(CourseAId);

        Assert.Equal(3, tags.Count);
        Assert.Contains($"course:{CourseAId:D}", tags);
        Assert.Contains(GrantTags.PlanAll(), tags);
        Assert.Contains($"plan:course:{CourseAId:D}", tags);
        Assert.DoesNotContain(tags, t => t.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

}
