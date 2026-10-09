using AccessService.Core.Database;
using AccessService.Core.Features.PlanGrants.IntegrationEvents;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using ContentAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SyncContentAccessTests : AccessServiceTestsBase
{
    public SyncContentAccessTests(IntegrationTestsWebFactory factory) : base(factory) { }

    // ───────────── Created handler ─────────────

    [Theory]
    [InlineData("FULL_ALL")]
    [InlineData("LEARN_ALL")]
    public async Task Full_or_learn_all_grant_writes_plan_all_only_to_redis(string tier)
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Plan plan = await CreateLifetimePlanAsync(authorId);
        if (string.Equals(tier, nameof(PlanTier.LEARN_ALL), StringComparison.Ordinal))
        {
            await ExecuteInDbAsync(async db =>
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE access.plans SET tier = 'LEARN_ALL', capabilities = 1 WHERE id = {plan.Id}"));
        }
        Guid grantId = await CreateGrantAsync(userId, plan.Id, PlanGrantStatus.ACTIVE);

        SyncContentAccessOnPlanGrantCreatedHandler handler = ResolveCreatedHandler();
        await handler.Handle(new PlanGrantCreated(
            GrantId: grantId,
            UserId: userId,
            PlanId: plan.Id,
            PlanTier: tier,
            PlanAuthorId: authorId,
            CourseId: null,
            IncludesFutureContent: true,
            Source: "INVITE_LINK",
            SourceRef: Guid.NewGuid(),
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null), CancellationToken.None);

        Assert.Contains(Factory.UserGrants.Granted,
            g => g.UserId == userId && g.Tag == GrantTags.PlanAll());
        Assert.DoesNotContain(Factory.UserGrants.Granted,
            g => g.UserId == userId && g.Tag.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Full_all_grant_does_not_emit_retired_capability()
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();
        Guid userId = Guid.NewGuid();
        Plan plan = await CreateLifetimePlanAsync(Guid.NewGuid());
        Guid grantId = await CreateGrantAsync(userId, plan.Id, PlanGrantStatus.ACTIVE);

        SyncContentAccessOnPlanGrantCreatedHandler handler = ResolveCreatedHandler();
        await handler.Handle(new PlanGrantCreated(
            GrantId: grantId,
            UserId: userId,
            PlanId: plan.Id,
            PlanTier: "FULL_ALL",
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            IncludesFutureContent: true,
            Source: "ADMIN_GRANT",
            SourceRef: Guid.NewGuid(),
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null), CancellationToken.None);

        Assert.DoesNotContain(Factory.UserGrants.Granted,
            g => g.UserId == userId && g.Tag == GrantTags.Capability(nameof(PlanCapabilities.TRAINER_PRO)));
        Assert.Contains(Factory.UserGrants.Granted,
            g => g.UserId == userId && g.Tag == GrantTags.PlanAll());
    }

    [Fact]
    public async Task COURSE_grant_writes_plan_course_tag()
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Plan plan = await CreateCoursePlanAsync(Guid.NewGuid(), courseId);
        Guid grantId = await CreateGrantAsync(userId, plan.Id, PlanGrantStatus.ACTIVE);

        SyncContentAccessOnPlanGrantCreatedHandler handler = ResolveCreatedHandler();
        await handler.Handle(new PlanGrantCreated(
            GrantId: grantId,
            UserId: userId,
            PlanId: plan.Id,
            PlanTier: "COURSE",
            PlanAuthorId: Guid.NewGuid(),
            CourseId: courseId,
            IncludesFutureContent: false,
            Source: "ADMIN_GRANT",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null), CancellationToken.None);

        Assert.Contains(Factory.UserGrants.Granted,
            g => g.UserId == userId && g.Tag == GrantTags.PlanCourse(courseId));
        Assert.DoesNotContain(Factory.UserGrants.Granted,
            g => g.Tag.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

    [Fact]
    public async Task COURSE_grant_with_null_course_id_is_noop()
    {
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();

        SyncContentAccessOnPlanGrantCreatedHandler handler = ResolveCreatedHandler();
        await handler.Handle(new PlanGrantCreated(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: "COURSE",
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            IncludesFutureContent: false,
            Source: "ADMIN_GRANT",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null), CancellationToken.None);

        Assert.Empty(Factory.UserGrants.Granted);
    }

    [Fact]
    public async Task Delayed_created_event_for_revoked_grant_does_not_restore_tags()
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateLifetimePlanAsync(Guid.NewGuid());
        Guid grantId = await CreateGrantAsync(userId, plan.Id, PlanGrantStatus.REVOKED);

        SyncContentAccessOnPlanGrantCreatedHandler handler = ResolveCreatedHandler();

        await handler.Handle(new PlanGrantCreated(
            grantId,
            userId,
            plan.Id,
            nameof(PlanTier.FULL_ALL),
            plan.AuthorId,
            CourseId: null,
            IncludesFutureContent: true,
            Source: nameof(PlanGrantSource.ADMIN_GRANT),
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null), CancellationToken.None);

        Assert.Empty(Factory.UserGrants.Snapshot(userId));
    }

    // ───────────── Revoked handler (recalc) ─────────────

    [Fact]
    public async Task PlanGrantRevoked_with_no_remaining_grants_clears_user_set()
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        // Создаём план + revoked grant — он не должен дать тегов.
        Plan plan = await CreateLifetimePlanAsync(authorId);
        await CreateGrantAsync(userId, plan.Id, status: PlanGrantStatus.REVOKED);

        SyncContentAccessOnPlanGrantRevokedHandler handler = ResolveRevokedHandler();
        await handler.Handle(new PlanGrantRevoked(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: plan.Id,
            Reason: "test",
            RevokedAt: DateTimeOffset.UtcNow), CancellationToken.None);

        IReadOnlySet<string> snapshot = Factory.UserGrants.Snapshot(userId);
        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task PlanGrantRevoked_keeps_tags_from_other_active_grants()
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        // Юзер имеет ACTIVE FULL plan и REVOKED FULL plan.
        // Revoke второго не должен задеть первого.
        Plan planA = await CreateLifetimePlanAsync(authorA);
        Plan planB = await CreateLifetimePlanAsync(authorB);

        await CreateGrantAsync(userId, planA.Id, status: PlanGrantStatus.ACTIVE);
        Guid revokedGrantId = await CreateGrantAsync(userId, planB.Id, status: PlanGrantStatus.REVOKED);

        SyncContentAccessOnPlanGrantRevokedHandler handler = ResolveRevokedHandler();
        await handler.Handle(new PlanGrantRevoked(
            GrantId: revokedGrantId,
            UserId: userId,
            PlanId: planB.Id,
            Reason: null,
            RevokedAt: DateTimeOffset.UtcNow), CancellationToken.None);

        IReadOnlySet<string> snapshot = Factory.UserGrants.Snapshot(userId);
        Assert.Contains(GrantTags.PlanAll(), snapshot);
        Assert.DoesNotContain(snapshot, tag => tag.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanGrantRevoked_overlap_keeps_shared_plan_all_tag_when_two_full_grants()
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        // Multi-grant overlap: два full-access плана дают один общий `plan:all`.
        // Revoke одного НЕ должен снимать тег — второй всё ещё ACTIVE.
        Plan plan1 = await CreateLifetimePlanAsync(authorId);
        Plan plan2 = await CreateLifetimePlanAsync(authorId);
        await CreateGrantAsync(userId, plan1.Id, status: PlanGrantStatus.ACTIVE);
        Guid revokedGrantId = await CreateGrantAsync(userId, plan2.Id, status: PlanGrantStatus.REVOKED);

        SyncContentAccessOnPlanGrantRevokedHandler handler = ResolveRevokedHandler();
        await handler.Handle(new PlanGrantRevoked(
            GrantId: revokedGrantId,
            UserId: userId,
            PlanId: plan2.Id,
            Reason: null,
            RevokedAt: DateTimeOffset.UtcNow), CancellationToken.None);

        IReadOnlySet<string> snapshot = Factory.UserGrants.Snapshot(userId);
        Assert.Contains(GrantTags.PlanAll(), snapshot);
        Assert.DoesNotContain(snapshot, tag => tag.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

    // ───────────── Expired handler (recalc) ─────────────

    [Fact]
    public async Task PlanGrantExpired_with_no_remaining_grants_clears_user_set()
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        Plan plan = await CreateLifetimePlanAsync(authorId);
        await CreateGrantAsync(userId, plan.Id, status: PlanGrantStatus.EXPIRED);

        SyncContentAccessOnPlanGrantExpiredHandler handler = ResolveExpiredHandler();
        await handler.Handle(new PlanGrantExpired(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: plan.Id,
            PlanTier: "FULL_ALL",
            PlanAuthorId: authorId,
            CourseId: null,
            ExpiredAt: DateTimeOffset.UtcNow), CancellationToken.None);

        IReadOnlySet<string> snapshot = Factory.UserGrants.Snapshot(userId);
        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task PlanGrantExpired_keeps_tags_from_other_active_grants()
    {
        await Factory.ResetDatabaseAsync();
        Factory.UserGrants.Reset();

        Guid userId = Guid.NewGuid();
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        Plan planA = await CreateLifetimePlanAsync(authorA);
        Plan planB = await CreateLifetimePlanAsync(authorB);
        await CreateGrantAsync(userId, planA.Id, status: PlanGrantStatus.ACTIVE);
        Guid expiredGrantId = await CreateGrantAsync(userId, planB.Id, status: PlanGrantStatus.EXPIRED);

        SyncContentAccessOnPlanGrantExpiredHandler handler = ResolveExpiredHandler();
        await handler.Handle(new PlanGrantExpired(
            GrantId: expiredGrantId,
            UserId: userId,
            PlanId: planB.Id,
            PlanTier: "FULL_ALL",
            PlanAuthorId: authorB,
            CourseId: null,
            ExpiredAt: DateTimeOffset.UtcNow), CancellationToken.None);

        IReadOnlySet<string> snapshot = Factory.UserGrants.Snapshot(userId);
        Assert.Contains(GrantTags.PlanAll(), snapshot);
        Assert.DoesNotContain(snapshot, tag => tag.StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal));
    }

    // ───────────── Helpers ─────────────

    private SyncContentAccessOnPlanGrantRevokedHandler ResolveRevokedHandler()
    {
        return new SyncContentAccessOnPlanGrantRevokedHandler(
            CreateProjection(),
            NullLogger<SyncContentAccessOnPlanGrantRevokedHandler>.Instance);
    }

    private SyncContentAccessOnPlanGrantExpiredHandler ResolveExpiredHandler()
    {
        return new SyncContentAccessOnPlanGrantExpiredHandler(
            CreateProjection(),
            NullLogger<SyncContentAccessOnPlanGrantExpiredHandler>.Instance);
    }

    private SyncContentAccessOnPlanGrantCreatedHandler ResolveCreatedHandler() =>
        new(
            CreateProjection(),
            NullLogger<SyncContentAccessOnPlanGrantCreatedHandler>.Instance);

    private IUserGrantProjection CreateProjection()
    {
        IServiceScope scope = Factory.Services.CreateScope();
        return new UserGrantProjection(
            scope.ServiceProvider.GetRequiredService<IPlanGrantsRepository>(),
            scope.ServiceProvider.GetRequiredService<IPlansRepository>(),
            Factory.UserGrants,
            TimeProvider.System);
    }

    private async Task<Plan> CreateLifetimePlanAsync(Guid authorId)
    {
        Plan plan = Plan.Create(
            authorId: authorId,
            tier: PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Test plan").Value,
            courseIds: [],
            requestedCapabilities: null).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });

        return plan;
    }

    private async Task<Plan> CreateCoursePlanAsync(Guid authorId, Guid courseId)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.COURSE,
            PlanSlug.Of($"course-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Course plan").Value,
            courseIds: [courseId],
            requestedCapabilities: null).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });

        return plan;
    }

    private async Task<Guid> CreateGrantAsync(Guid userId, Guid planId, PlanGrantStatus status)
    {
        PlanGrant grant = PlanGrant.Create(userId, planId, PlanGrantSource.ADMIN_GRANT, sourceRef: null);
        if (status == PlanGrantStatus.REVOKED)
            grant.Revoke(Guid.NewGuid(), "test");
        else if (status == PlanGrantStatus.EXPIRED)
            grant.Expire();

        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        return grant.Id;
    }
}