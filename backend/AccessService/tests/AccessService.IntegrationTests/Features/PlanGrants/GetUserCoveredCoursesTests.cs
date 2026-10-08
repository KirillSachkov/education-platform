using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Domain;
using AccessService.Infrastructure.Postgres;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
/// Phase 0 derive read-model: <c>POST /internal/access/users/{id}/covered-courses</c>.
/// Set покрытых курсов = explicit COURSE ∪ все курсы платформы для FULL_ALL/LEARN_ALL
/// ∪ legacy FREE-авторы. Опциональный authorId-фильтр сужает до курсов одного автора.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetUserCoveredCoursesTests : AccessServiceTestsBase
{
    public GetUserCoveredCoursesTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Full_all_grant_expands_to_all_platform_courses()
    {
        Guid author = Guid.NewGuid();
        Guid c1 = Guid.NewGuid();
        Guid c2 = Guid.NewGuid();
        Factory.EduClient.AllCourseIds = [c1, c2];

        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, author);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.ADMIN_GRANT);

        CoveredCoursesResult result = await GetCoveredAsync(user, authorId: null);

        Assert.Equal(2, result.CourseIds.Count);
        Assert.Contains(c1, result.CourseIds);
        Assert.Contains(c2, result.CourseIds);
    }

    [Fact]
    public async Task Learn_all_grant_expands_to_all_platform_courses()
    {
        Guid author = Guid.NewGuid();
        Guid c1 = Guid.NewGuid();
        Guid c2 = Guid.NewGuid();
        Factory.EduClient.AllCourseIds = [c1, c2];

        Guid planId = await SeedLegacyPlanRawAsync("LEARN_ALL", author);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.ADMIN_GRANT);

        CoveredCoursesResult result = await GetCoveredAsync(user, authorId: null);

        Assert.Equal(2, result.CourseIds.Count);
        Assert.Contains(c1, result.CourseIds);
        Assert.Contains(c2, result.CourseIds);
    }

    [Fact]
    public async Task Course_grant_yields_only_that_course()
    {
        Guid author = Guid.NewGuid();
        Guid course = Guid.NewGuid();

        Guid planId = await SeedPlanAsync(PlanTier.COURSE, author, courseId: course);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.INVITE_LINK);

        CoveredCoursesResult result = await GetCoveredAsync(user, authorId: null);

        Assert.Equal([course], result.CourseIds);
    }

    [Fact]
    public async Task Course_and_full_all_grants_union()
    {
        Guid author = Guid.NewGuid();
        Guid globalCourse = Guid.NewGuid();
        Guid explicitCourse = Guid.NewGuid();
        Factory.EduClient.AllCourseIds = [globalCourse];

        Guid fullPlan = await SeedPlanAsync(PlanTier.FULL_ALL, author);
        Guid coursePlan = await SeedPlanAsync(PlanTier.COURSE, author, courseId: explicitCourse);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(fullPlan, user, PlanGrantSource.ADMIN_GRANT);
        await SeedGrantAsync(coursePlan, user, PlanGrantSource.INVITE_LINK);

        CoveredCoursesResult result = await GetCoveredAsync(user, authorId: null);

        Assert.Equal(2, result.CourseIds.Count);
        Assert.Contains(globalCourse, result.CourseIds);
        Assert.Contains(explicitCourse, result.CourseIds);
    }

    [Fact]
    public async Task Full_all_grant_covers_transferred_course_without_author_binding()
    {
        Guid previousAuthor = Guid.NewGuid();
        Guid newAuthor = Guid.NewGuid();
        Guid transferredCourse = Guid.NewGuid();
        Factory.EduClient.AuthorCourseIds[previousAuthor] = [];
        Factory.EduClient.AuthorCourseIds[newAuthor] = [transferredCourse];

        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, previousAuthor);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.ADMIN_GRANT);

        CoveredCoursesResult result = await GetCoveredAsync(user, authorId: newAuthor);

        Assert.Equal([transferredCourse], result.CourseIds);
    }

    [Fact]
    public async Task AuthorId_filter_narrows_global_access_to_that_authors_courses()
    {
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid courseA = Guid.NewGuid();
        Guid courseB = Guid.NewGuid();
        Factory.EduClient.AllCourseIds = [courseA, courseB];
        Factory.EduClient.AuthorCourseIds[authorA] = [courseA];
        Factory.EduClient.AuthorCourseIds[authorB] = [courseB];

        Guid plan = await SeedPlanAsync(PlanTier.FULL_ALL, authorA);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(plan, user, PlanGrantSource.ADMIN_GRANT);

        CoveredCoursesResult unfiltered = await GetCoveredAsync(user, authorId: null);
        Assert.Equal(2, unfiltered.CourseIds.Count);

        CoveredCoursesResult filtered = await GetCoveredAsync(user, authorId: authorA);
        Assert.Equal([courseA], filtered.CourseIds);
    }

    [Fact]
    public async Task Revoked_grant_does_not_contribute()
    {
        Guid author = Guid.NewGuid();
        Guid course = Guid.NewGuid();
        Factory.EduClient.AllCourseIds = [course];

        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, author);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.ADMIN_GRANT,
            mutate: g => g.Revoke(Guid.NewGuid(), "test"));

        CoveredCoursesResult result = await GetCoveredAsync(user, authorId: null);

        Assert.Empty(result.CourseIds);
    }

    [Fact]
    public async Task No_grants_returns_empty()
    {
        CoveredCoursesResult result = await GetCoveredAsync(Guid.NewGuid(), authorId: null);
        Assert.Empty(result.CourseIds);
    }

    [Fact]
    public async Task Author_role_cannot_call_internal_endpoint()
    {
        // Default test identity is platform-author.
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/internal/access/users/{Guid.NewGuid()}/covered-courses",
            new CoveredCoursesRequest(null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────

    private async Task<CoveredCoursesResult> GetCoveredAsync(Guid userId, Guid? authorId)
    {
        AuthenticateAs("platform-service", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/internal/access/users/{userId}/covered-courses",
            new CoveredCoursesRequest(authorId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<CoveredCoursesResult>>())!.Result!;
    }

    private async Task<Guid> SeedPlanAsync(PlanTier tier, Guid authorId, Guid? courseId = null)
    {
        Guid planId = Guid.Empty;
        await ExecuteInDbAsync(async db =>
        {
            PlanSlug slug = PlanSlug.Of($"seed-{Guid.NewGuid():N}").Value;
            PlanDisplayName name = PlanDisplayName.Of("Seed plan").Value;
            Plan plan = Plan.Create(authorId, tier, slug, name, courseId is { } __cc ? [__cc] : [], null).Value;
            await db.Plans.AddAsync(plan);
            await db.SaveChangesAsync();
            planId = plan.Id;
        });
        return planId;
    }

    private async Task SeedGrantAsync(
        Guid planId, Guid userId, PlanGrantSource source, Action<PlanGrant>? mutate = null)
    {
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(userId, planId, source, sourceRef: null);
            mutate?.Invoke(grant);
            await db.PlanGrants.AddAsync(grant);
            await db.SaveChangesAsync();
        });
    }

    private async Task<Guid> SeedLegacyPlanRawAsync(string tier, Guid authorId)
    {
        Guid planId = Guid.CreateVersion7();
        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO access.plans
                    (id, author_id, tier, slug, display_name, short_description,
                     long_description, features, currency, includes_future_content,
                     capabilities, term_kind, is_public, is_active, is_highlighted,
                     display_order, created_at)
                VALUES
                    ({planId}, {authorId}, {tier}, {"legacy-" + planId.ToString("N")},
                     'Legacy plan', '', '', '[]'::jsonb, 'RUB', true,
                     1, 'LIFETIME', true, true, false, 0, now())
                """);
        });
        return planId;
    }
}
