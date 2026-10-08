using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
///     #687 AC7 — <c>GET /access/me/access-status/</c>. Питает модалку «доступ истёк»:
///     <see cref="AccessStatusDto.HasActiveAccess"/> + недавно (≤30д) истёкший grant, scope
///     которого больше НЕ покрыт активным grant'ом (<see cref="AccessStatusDto.RecentlyExpired"/>).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMyAccessStatusTests : AccessServiceTestsBase
{
    private const string Path = "/access/me/access-status/";

    public GetMyAccessStatusTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        RemoveAuthentication();
        HttpResponseMessage resp = await AppHttpClient.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Recently_expired_with_no_active_returns_expired_summary()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan trial = await SeedFullAllPlanAsync(authorId, "Пробный месяц", trialDurationDays: 30);
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(-5); // within 30-day window
        await SeedExpiredGrantAsync(userId, trial.Id, expiresAt);

        AccessStatusDto dto = await GetStatusAsync(userId);

        Assert.False(dto.HasActiveAccess);
        Assert.NotNull(dto.RecentlyExpired);
        Assert.Equal(trial.Id, dto.RecentlyExpired!.PlanId);
        Assert.Equal("Пробный месяц", dto.RecentlyExpired.PlanName);
        Assert.Equal(nameof(PlanTier.FULL_ALL), dto.RecentlyExpired.Tier);
        Assert.True(
            (dto.RecentlyExpired.ExpiredAt - expiresAt).Duration() < TimeSpan.FromSeconds(5),
            $"ExpiredAt {dto.RecentlyExpired.ExpiredAt:O} should equal grant.ExpiresAt {expiresAt:O}");
    }

    [Fact]
    public async Task Active_grant_covering_expired_scope_hides_recently_expired()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan trial = await SeedFullAllPlanAsync(authorId, "Пробный месяц", trialDurationDays: 30);
        await SeedExpiredGrantAsync(userId, trial.Id, DateTimeOffset.UtcNow.AddDays(-5));

        // An active lifetime FULL_ALL grant fully covers the expired trial's scope.
        Plan lifetime = await SeedFullAllPlanAsync(authorId, "Полный доступ");
        await SeedActiveGrantAsync(userId, lifetime.Id, expiresAt: null);

        AccessStatusDto dto = await GetStatusAsync(userId);

        Assert.True(dto.HasActiveAccess);
        Assert.Null(dto.RecentlyExpired);
    }

    [Fact]
    public async Task Only_active_grants_no_expired_returns_null_recently_expired()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan lifetime = await SeedFullAllPlanAsync(authorId, "Полный доступ");
        await SeedActiveGrantAsync(userId, lifetime.Id, expiresAt: null);

        AccessStatusDto dto = await GetStatusAsync(userId);

        Assert.True(dto.HasActiveAccess);
        Assert.Null(dto.RecentlyExpired);
    }

    [Fact]
    public async Task Expired_beyond_window_is_not_reported()
    {
        // Boundary: an expiry older than the 30-day window is not "recently expired".
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan trial = await SeedFullAllPlanAsync(authorId, "Пробный месяц", trialDurationDays: 30);
        await SeedExpiredGrantAsync(userId, trial.Id, DateTimeOffset.UtcNow.AddDays(-45));

        AccessStatusDto dto = await GetStatusAsync(userId);

        Assert.False(dto.HasActiveAccess);
        Assert.Null(dto.RecentlyExpired);
    }

    [Fact]
    public async Task Active_and_uncovered_recently_expired_are_independent()
    {
        // HasActiveAccess and RecentlyExpired are independent axes: a user can hold an active
        // COURSE grant (course X) AND have a recently-expired COURSE grant (course Y) whose
        // scope the active grant does NOT cover.
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan activeCourse = await SeedCoursePlanAsync(authorId, "Курс X");
        await SeedActiveGrantAsync(userId, activeCourse.Id, expiresAt: null);

        Plan expiredCourse = await SeedCoursePlanAsync(authorId, "Курс Y");
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(-3);
        await SeedExpiredGrantAsync(userId, expiredCourse.Id, expiresAt);

        AccessStatusDto dto = await GetStatusAsync(userId);

        Assert.True(dto.HasActiveAccess);
        Assert.NotNull(dto.RecentlyExpired);
        Assert.Equal(expiredCourse.Id, dto.RecentlyExpired!.PlanId);
        Assert.Equal(nameof(PlanTier.COURSE), dto.RecentlyExpired.Tier);
    }

    private async Task<AccessStatusDto> GetStatusAsync(Guid userId)
    {
        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(Path);
        resp.EnsureSuccessStatusCode();
        AccessStatusDto? dto = (await resp.Content.ReadFromJsonAsync<Envelope<AccessStatusDto>>())!.Result;
        Assert.NotNull(dto);
        return dto!;
    }

    private async Task<Plan> SeedFullAllPlanAsync(Guid authorId, string displayName, int? trialDurationDays = null)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.FULL_ALL,
            PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of(displayName).Value,
            courseIds: [],
            requestedCapabilities: null,
            offerType: null,
            trialDurationDays: trialDurationDays).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan;
    }

    private async Task<Plan> SeedCoursePlanAsync(Guid authorId, string displayName)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.COURSE,
            PlanSlug.Of($"course-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of(displayName).Value,
            courseIds: [Guid.NewGuid()],
            requestedCapabilities: null).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan;
    }

    private async Task SeedActiveGrantAsync(Guid userId, Guid planId, DateTimeOffset? expiresAt)
    {
        PlanGrant grant = PlanGrant.Create(
            userId, planId, PlanGrantSource.PURCHASE, sourceRef: Guid.NewGuid(), expiresAt: expiresAt);
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedExpiredGrantAsync(Guid userId, Guid planId, DateTimeOffset expiresAt)
    {
        PlanGrant grant = PlanGrant.Create(
            userId, planId, PlanGrantSource.PURCHASE, sourceRef: Guid.NewGuid(),
            expiresAt: expiresAt, pricePaidCents: 1_000);
        grant.Expire();
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }
}
