using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
///     #687 Bug B — <c>POST /internal/access/grants/by-plan</c>:
///     (a) auto-источник (TELEGRAM_F1) на ТРИАЛ-плане выдаёт time-limited grant (TTL = TrialDurationDays),
///         а не бессрочный;
///     (b) scope-aware dedup — auto-источник возвращает покрывающий grant вместо нового, если scope
///         уже покрыт активным grant'ом на другом плане;
///     (c) ADMIN_GRANT — осознанное действие, scope-gate'ом не блокируется.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GrantByPlanScopeDedupTests : AccessServiceTestsBase
{
    private const int TrialDurationDays = 30;

    public GrantByPlanScopeDedupTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Telegram_f1_on_trial_plan_issues_time_limited_grant()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Plan trial = await SeedFullAllPlanAsync(authorId, trialDurationDays: TrialDurationDays);

        AuthenticateAs("platform-service", Guid.NewGuid());

        DateTimeOffset before = DateTimeOffset.UtcNow;
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/by-plan",
            new GrantByPlanRequest(userId, trial.Id, nameof(PlanGrantSource.TELEGRAM_F1), null));
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        PlanGrantDto dto = (await resp.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!;

        // The trial plan must yield a TTL grant (~ now + 30 days), NOT a perpetual one.
        Assert.NotNull(dto.ExpiresAt);
        Assert.InRange(
            dto.ExpiresAt!.Value,
            before.AddDays(TrialDurationDays).AddSeconds(-5),
            after.AddDays(TrialDurationDays).AddSeconds(5));

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.SingleAsync(g => g.UserId == userId && g.PlanId == trial.Id);
            Assert.Equal(PlanGrantSource.TELEGRAM_F1, grant.Source);
            Assert.NotNull(grant.ExpiresAt);
        });
    }

    [Fact]
    public async Task Telegram_f1_skips_when_scope_already_covered_and_returns_covering_grant()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        // User holds a (time-limited) covering FULL_ALL grant on plan A.
        Plan coveringA = await SeedFullAllPlanAsync(authorId);
        await SeedGrantAsync(userId, coveringA.Id, PlanGrantSource.PURCHASE,
            expiresAt: DateTimeOffset.UtcNow.AddDays(30));

        // by-plan tries to grant a DIFFERENT trial FULL_ALL plan B via TELEGRAM_F1.
        Plan trialB = await SeedFullAllPlanAsync(authorId, trialDurationDays: TrialDurationDays);

        AuthenticateAs("platform-service", Guid.NewGuid());
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/by-plan",
            new GrantByPlanRequest(userId, trialB.Id, nameof(PlanGrantSource.TELEGRAM_F1), null));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        PlanGrantDto dto = (await resp.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!;

        // Returns the existing covering grant on A — NOT a fresh grant on B.
        Assert.Equal(coveringA.Id, dto.PlanId);

        await ExecuteInDbAsync(async db =>
        {
            Assert.False(await db.PlanGrants.AnyAsync(g => g.UserId == userId && g.PlanId == trialB.Id));
            int total = await db.PlanGrants.CountAsync(g => g.UserId == userId);
            Assert.Equal(1, total);
        });
    }

    [Fact]
    public async Task Admin_grant_is_not_scope_gated_and_still_creates()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        // User already holds an active covering FULL_ALL grant on plan A.
        Plan coveringA = await SeedFullAllPlanAsync(authorId);
        await SeedGrantAsync(userId, coveringA.Id, PlanGrantSource.PURCHASE, expiresAt: null);

        // ADMIN_GRANT on a narrower COURSE plan B (scope ⊆ FULL_ALL) — must NOT be deduped.
        Plan courseB = await SeedCoursePlanAsync(authorId);

        AuthenticateAs("platform-service", Guid.NewGuid());
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/by-plan",
            new GrantByPlanRequest(userId, courseB.Id, nameof(PlanGrantSource.ADMIN_GRANT), null));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.SingleAsync(g => g.UserId == userId && g.PlanId == courseB.Id);
            Assert.Equal(PlanGrantSource.ADMIN_GRANT, grant.Source);

            int total = await db.PlanGrants.CountAsync(g => g.UserId == userId);
            Assert.Equal(2, total); // covering grant + the new admin grant
        });
    }

    private async Task<Plan> SeedFullAllPlanAsync(Guid authorId, int? trialDurationDays = null)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.FULL_ALL,
            PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Plan").Value,
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

    private async Task<Plan> SeedCoursePlanAsync(Guid authorId)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.COURSE,
            PlanSlug.Of($"course-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Course plan").Value,
            courseIds: [Guid.NewGuid()],
            requestedCapabilities: null).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan;
    }

    private async Task SeedGrantAsync(
        Guid userId, Guid planId, PlanGrantSource source, DateTimeOffset? expiresAt)
    {
        PlanGrant grant = PlanGrant.Create(userId, planId, source, sourceRef: null, expiresAt: expiresAt);
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }
}
