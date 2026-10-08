using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Domain;
using AccessService.Infrastructure.Postgres;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
/// Phase 0 derive read-model: <c>GET /internal/access/courses/{id}/grantees</c>.
/// Roster = пользователи с активным grant'ом, покрывающим курс (global FULL/LEARN ∪
/// COURSE-of-course; FREE исключён, revoked/expired исключены). Keyset-пагинация +
/// name-search через userIds-фильтр.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetCourseGranteesTests : AccessServiceTestsBase
{
    private static readonly Guid AuthorId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CourseId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid OtherCourseId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    public GetCourseGranteesTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Lifetime_grant_user_is_in_roster()
    {
        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, AuthorId);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.ADMIN_GRANT);

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Single(page.Items);
        Assert.Equal(user, page.Items[0].UserId);
        Assert.Equal(nameof(PlanTier.FULL_ALL), page.Items[0].PlanTier);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Full_all_grant_from_another_author_is_in_roster()
    {
        Guid foreignAuthor = Guid.NewGuid();
        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, foreignAuthor);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.ADMIN_GRANT);

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Single(page.Items);
        Assert.Equal(user, page.Items[0].UserId);
        Assert.Equal(nameof(PlanTier.FULL_ALL), page.Items[0].PlanTier);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Learn_all_grant_from_another_author_is_in_roster()
    {
        Guid foreignAuthor = Guid.NewGuid();
        Guid planId = await SeedLegacyPlanRawAsync("LEARN_ALL", foreignAuthor);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.ADMIN_GRANT);

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Single(page.Items);
        Assert.Equal(user, page.Items[0].UserId);
        Assert.Equal(nameof(PlanTier.LEARN_ALL), page.Items[0].PlanTier);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Lifetime_grantees_endpoint_returns_global_full_access_users()
    {
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid planA = await SeedPlanAsync(PlanTier.FULL_ALL, authorA);
        Guid planB = await SeedPlanAsync(PlanTier.FULL_ALL, authorB);
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        await SeedGrantAsync(planA, userA, PlanGrantSource.ADMIN_GRANT);
        await SeedGrantAsync(planB, userB, PlanGrantSource.ADMIN_GRANT);

        AuthenticateAs("platform-service", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/users/with-lifetime/{authorA}");

        response.EnsureSuccessStatusCode();
        IReadOnlyList<Guid> userIds = (await response.Content
            .ReadFromJsonAsync<Envelope<IReadOnlyList<Guid>>>())!.Result!;

        Assert.Equal(2, userIds.Count);
        Assert.Contains(userA, userIds);
        Assert.Contains(userB, userIds);
    }

    [Fact]
    public async Task Course_grant_user_is_in_roster()
    {
        Guid planId = await SeedPlanAsync(PlanTier.COURSE, AuthorId, courseId: CourseId);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.INVITE_LINK);

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Single(page.Items);
        Assert.Equal(user, page.Items[0].UserId);
        Assert.Equal(nameof(PlanTier.COURSE), page.Items[0].PlanTier);
    }

    [Fact]
    public async Task Course_grant_for_other_course_is_not_in_roster()
    {
        Guid planId = await SeedPlanAsync(PlanTier.COURSE, AuthorId, courseId: OtherCourseId);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.INVITE_LINK);

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Free_grant_is_not_in_roster()
    {
        Guid freePlanId = await SeedFreePlanRawAsync(AuthorId);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(freePlanId, user, PlanGrantSource.TRIAL);

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Revoked_and_expired_grants_are_excluded()
    {
        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, AuthorId);

        Guid revokedUser = Guid.NewGuid();
        await SeedGrantAsync(planId, revokedUser, PlanGrantSource.ADMIN_GRANT,
            mutate: g => g.Revoke(Guid.NewGuid(), "test"));

        Guid expiredUser = Guid.NewGuid();
        await SeedGrantAsync(planId, expiredUser, PlanGrantSource.ADMIN_GRANT,
            mutate: g => g.Expire());

        Guid activeUser = Guid.NewGuid();
        await SeedGrantAsync(planId, activeUser, PlanGrantSource.ADMIN_GRANT);

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Single(page.Items);
        Assert.Equal(activeUser, page.Items[0].UserId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Archived_plan_grants_are_excluded()
    {
        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, AuthorId);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(planId, user, PlanGrantSource.ADMIN_GRANT);

        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE access.plans SET archived_at = now(), is_active = false WHERE id = {planId}");
        });

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task User_with_both_course_and_lifetime_grant_appears_once()
    {
        Guid lifetimePlan = await SeedPlanAsync(PlanTier.FULL_ALL, AuthorId);
        Guid coursePlan = await SeedPlanAsync(PlanTier.COURSE, AuthorId, courseId: CourseId);
        Guid user = Guid.NewGuid();
        await SeedGrantAsync(lifetimePlan, user, PlanGrantSource.ADMIN_GRANT);
        await SeedGrantAsync(coursePlan, user, PlanGrantSource.INVITE_LINK);

        CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId);

        Assert.Single(page.Items);
        Assert.Equal(user, page.Items[0].UserId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Keyset_pagination_walks_all_grantees()
    {
        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, AuthorId);
        var users = new List<Guid>();
        for (int i = 0; i < 5; i++)
        {
            Guid u = Guid.NewGuid();
            users.Add(u);
            await SeedGrantAsync(planId, u, PlanGrantSource.ADMIN_GRANT);
        }

        var seen = new HashSet<Guid>();
        string? cursor = null;
        int pages = 0;
        do
        {
            CourseGranteesPage page = await GetGranteesAsync(CourseId, AuthorId, cursor: cursor, limit: 2);
            Assert.Equal(5, page.TotalCount);
            foreach (CourseGranteeDto item in page.Items)
            {
                Assert.True(seen.Add(item.UserId), "duplicate user across pages");
            }
            cursor = page.NextCursor;
            pages++;
            Assert.True(pages <= 5, "keyset did not terminate");
        }
        while (cursor is not null);

        Assert.Equal(users.Count, seen.Count);
        Assert.True(users.TrueForAll(seen.Contains));
    }

    [Fact]
    public async Task UserIds_filter_narrows_roster()
    {
        Guid planId = await SeedPlanAsync(PlanTier.FULL_ALL, AuthorId);
        Guid wanted = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        await SeedGrantAsync(planId, wanted, PlanGrantSource.ADMIN_GRANT);
        await SeedGrantAsync(planId, other, PlanGrantSource.ADMIN_GRANT);

        Factory.EduClient.CourseAuthorsById[CourseId] = AuthorId;
        AuthenticateAs("platform-service", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/courses/{CourseId}/grantees?authorId={AuthorId}&userIds={wanted}");
        response.EnsureSuccessStatusCode();
        CourseGranteesPage page = (await response.Content
            .ReadFromJsonAsync<Envelope<CourseGranteesPage>>())!.Result!;

        Assert.Single(page.Items);
        Assert.Equal(wanted, page.Items[0].UserId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Author_role_cannot_call_internal_endpoint()
    {
        // Default test identity is platform-author.
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/courses/{CourseId}/grantees?authorId={AuthorId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Mismatched_caller_authorId_cannot_surface_foreign_authors_grantees()
    {
        // The course belongs to AuthorId. A caller passing a DIFFERENT authorId violates the
        // route contract. The handler resolves the authoritative author from courseId, sees
        // the mismatch, and returns 400.
        Guid foreignAuthor = Guid.NewGuid();
        Guid foreignPlan = await SeedPlanAsync(PlanTier.FULL_ALL, foreignAuthor);
        Guid foreignGrantee = Guid.NewGuid();
        await SeedGrantAsync(foreignPlan, foreignGrantee, PlanGrantSource.ADMIN_GRANT);

        // The real owner of CourseId is AuthorId (not the foreign author).
        Factory.EduClient.CourseAuthorsById[CourseId] = AuthorId;
        AuthenticateAs("platform-service", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/courses/{CourseId}/grantees?authorId={foreignAuthor}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(foreignGrantee.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    // ── seed helpers ──────────────────────────────────────────────

    private async Task<CourseGranteesPage> GetGranteesAsync(
        Guid courseId, Guid authorId, string? cursor = null, int? limit = null)
    {
        // Handler resolves authorId server-side from courseId via the fake ECS — register the
        // course → author mapping so the resolved author matches the caller-supplied one.
        Factory.EduClient.CourseAuthorsById[courseId] = authorId;

        AuthenticateAs("platform-service", Guid.NewGuid());

        string url = $"/internal/access/courses/{courseId}/grantees?authorId={authorId}";
        if (cursor is not null) url += $"&cursor={Uri.EscapeDataString(cursor)}";
        if (limit is { } l) url += $"&limit={l}";

        HttpResponseMessage response = await AppHttpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<CourseGranteesPage>>())!.Result!;
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

    /// <summary>
    /// FREE plans cannot be created via <see cref="Plan.Create"/> (deprecated #358), so
    /// seed the legacy row via raw SQL covering all NOT NULL columns.
    /// </summary>
    private Task<Guid> SeedFreePlanRawAsync(Guid authorId) => SeedLegacyPlanRawAsync("FREE", authorId);

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
                     'Legacy free', '', '', '[]'::jsonb, 'RUB', true,
                     1, 'LIFETIME', true, true, false, 0, now())
                """);
        });
        return planId;
    }
}
