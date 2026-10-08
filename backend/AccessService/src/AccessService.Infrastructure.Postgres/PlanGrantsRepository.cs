using System.Linq.Expressions;
using AccessService.Core.Database;
using AccessService.Domain;

namespace AccessService.Infrastructure.Postgres;

internal sealed class PlanGrantsRepository : IPlanGrantsRepository
{
    private readonly AccessServiceDbContext _db;

    public PlanGrantsRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(PlanGrant grant, CancellationToken ct = default) =>
        await _db.PlanGrants.AddAsync(grant, ct);

    public async Task<Result<PlanGrant, Error>> GetByAsync(
        Expression<Func<PlanGrant, bool>> predicate,
        CancellationToken ct = default)
    {
        PlanGrant? grant = await _db.PlanGrants.FirstOrDefaultAsync(predicate, ct);
        return grant is null ? AccessErrors.GrantNotFound() : grant;
    }

    public async Task<Result<PlanGrant, Error>> GetActiveForUpdateAsync(
        Guid userId,
        Guid planId,
        CancellationToken ct = default)
    {
        PlanGrant? grant = await _db.PlanGrants
            .FromSqlInterpolated($$"""
                SELECT *, xmin
                FROM access.plan_grants
                WHERE user_id = {{userId}}
                  AND plan_id = {{planId}}
                  AND status = 'ACTIVE'
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);
        return grant is null ? AccessErrors.GrantNotFound() : grant;
    }

    public async Task<IReadOnlyList<PlanGrant>> GetManyByAsync(
        Expression<Func<PlanGrant, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.PlanGrants.Where(predicate).ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        Expression<Func<PlanGrant, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.PlanGrants.AnyAsync(predicate, ct);

    public async Task<IReadOnlyList<Guid>> GetActiveUserIdsByPlanBatchAsync(
        Guid planId,
        Guid? afterUserId,
        int take,
        CancellationToken ct = default)
    {
        IQueryable<Guid> query = _db.PlanGrants
            .AsNoTracking()
            .Where(grant => grant.PlanId == planId && grant.Status == PlanGrantStatus.ACTIVE)
            .Select(grant => grant.UserId)
            .Distinct();

        if (afterUserId.HasValue)
        {
            Guid cursor = afterUserId.Value;
            query = query.Where(userId => userId > cursor);
        }

        return await query
            .OrderBy(userId => userId)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PlanGrant>> GetExpiringBatchAsync(
        Expression<Func<PlanGrant, bool>> predicate,
        int take,
        CancellationToken ct = default) =>
        await _db.PlanGrants
            .Where(predicate)
            .OrderBy(g => g.ExpiresAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PlanGrant>> GetDueRecurringBatchAsync(
        DateTimeOffset now,
        int take,
        CancellationToken ct = default)
    {
        DateTimeOffset hardGraceCutoff = now.Subtract(SubscriptionRenewalPolicy.GracePeriod);
        return await _db.PlanGrants
            .Where(g => g.Status == PlanGrantStatus.ACTIVE
                && g.RebillId != null
                && g.CustomerKey != null
                && g.ExpiresAt != null
                && g.NextChargeAt != null
                && g.NextChargeAt <= now
                && g.AutoRenewalCancelledAt == null
                && (g.RenewalGraceEndsAt != null
                    ? g.RenewalGraceEndsAt > now
                    : g.ExpiresAt > hardGraceCutoff)
                && g.ChargeFailureCount < SubscriptionRenewalPolicy.MAX_ATTEMPTS
                && _db.Plans.Any(p => p.Id == g.PlanId
                    && p.IsActive
                    && p.ArchivedAt == null
                    && p.Term.Kind == PlanTermKind.RECURRING
                    && p.Term.RecurringIntervalDays > 0
                    && p.PriceCents > 0))
            .OrderBy(g => g.NextChargeAt)
            .ThenBy(g => g.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PlanGrant>> GetEffectiveExpiringBatchAsync(
        DateTimeOffset cutoff,
        int take,
        CancellationToken ct = default)
    {
        DateTimeOffset hardGraceCutoff = cutoff.Subtract(SubscriptionRenewalPolicy.GracePeriod);
        return await _db.PlanGrants
            .Where(g => g.Status == PlanGrantStatus.ACTIVE
                && g.ExpiresAt != null
                && (g.RebillId != null
                    && g.CustomerKey != null
                    && g.AutoRenewalCancelledAt == null
                        ? g.RenewalGraceEndsAt != null
                            ? g.RenewalGraceEndsAt <= cutoff
                            : g.ExpiresAt <= hardGraceCutoff
                        : g.ExpiresAt <= cutoff))
            .OrderBy(g => g.RenewalGraceEndsAt ?? g.ExpiresAt)
            .ThenBy(g => g.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PlanGrant>> GetPlanGrantsKeysetAsync(
        Guid planId,
        IReadOnlyList<Guid>? userIdsFilter,
        DateTimeOffset? cursorGrantedAt,
        Guid? cursorId,
        int limit,
        CancellationToken ct = default)
    {
        IQueryable<PlanGrant> query = _db.PlanGrants
            .AsNoTracking()
            .Where(g => g.PlanId == planId);

        if (userIdsFilter is { Count: > 0 })
        {
            query = query.Where(g => userIdsFilter.Contains(g.UserId));
        }

        // Keyset: ORDER BY granted_at DESC, id DESC; cursor — pair (grantedAt, id).
        // EF Npgsql translates `Guid <`/`>` to native UUID comparison.
        if (cursorGrantedAt.HasValue && cursorId.HasValue)
        {
            DateTimeOffset cgAt = cursorGrantedAt.Value;
            Guid cgId = cursorId.Value;
            query = query.Where(g => g.GrantedAt < cgAt || (g.GrantedAt == cgAt && g.Id < cgId));
        }

        return await query
            .OrderByDescending(g => g.GrantedAt)
            .ThenByDescending(g => g.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task AddRedemptionAsync(InviteRedemption redemption, CancellationToken ct = default) =>
        await _db.InviteRedemptions.AddAsync(redemption, ct);

    public async Task<IReadOnlyList<Guid>> GetActiveLifetimeUserIdsByAuthorAsync(
        Guid authorId,
        CancellationToken ct = default) =>
        await (from grant in _db.PlanGrants.AsNoTracking()
               join plan in _db.Plans.AsNoTracking() on grant.PlanId equals plan.Id
               where grant.Status == PlanGrantStatus.ACTIVE
                  && (plan.Tier == PlanTier.FULL_ALL || plan.Tier == PlanTier.LEARN_ALL)
                  && plan.IsActive
                  && plan.ArchivedAt == null
               select grant.UserId)
              .Distinct()
              .ToListAsync(ct);

    public async Task<IReadOnlyList<CourseGranteeRow>> GetCourseGranteesKeysetAsync(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter,
        DateTimeOffset? cursorGrantedAt,
        Guid? cursorId,
        int limit,
        CancellationToken ct = default)
    {
        // Per-user keyset over Max(GrantedAt) — Postgres has no max(uuid), so we keyset on
        // the user's latest covering-grant timestamp and break ties by UserId (both plain
        // columns, fully translatable). DISTINCT-per-user is required because a user may
        // hold both a COURSE and a lifetime grant covering the same course. The aggregate
        // is projected to an anonymous type (not a constructed record) so the subsequent
        // OrderBy on its members translates to a PostgreSQL ORDER BY.
        var keyset = BuildUserKeysetGrouping(courseId, authorId, userIdsFilter)
            .Select(g => new { UserId = g.Key, GrantedAt = g.Max(x => x.GrantedAt) });

        // Cursor encodes (GrantedAt, UserId) — the keyset's ordering tuple.
        if (cursorGrantedAt.HasValue && cursorId.HasValue)
        {
            DateTimeOffset cgAt = cursorGrantedAt.Value;
            Guid cgUser = cursorId.Value;
            keyset = keyset.Where(k => k.GrantedAt < cgAt || (k.GrantedAt == cgAt && k.UserId < cgUser));
        }

        var pageUsers = await keyset
            .OrderByDescending(k => k.GrantedAt)
            .ThenByDescending(k => k.UserId)
            .Take(limit)
            .ToListAsync(ct);

        if (pageUsers.Count == 0)
        {
            return [];
        }

        // Resolve grant metadata (source/tier/grantId) for the page's users. The page is
        // bounded (<= limit), so this is one extra indexed query; pick each user's latest
        // covering grant in memory. max(uuid) is unavailable, so we cannot do this purely
        // in SQL — but the working set is a single page.
        List<Guid> pageUserIds = pageUsers.Select(k => k.UserId).ToList();

        // COURSE plans covering this course resolve via the plan_courses join (#404 bundle).
        IQueryable<Guid> coursePlanIds = _db.PlanCourses.AsNoTracking()
            .Where(pc => pc.CourseId == courseId)
            .Select(pc => pc.PlanId);

        List<GrantMeta> metaRows = await (
                from grant in _db.PlanGrants.AsNoTracking()
                join plan in _db.Plans.AsNoTracking() on grant.PlanId equals plan.Id
                where grant.Status == PlanGrantStatus.ACTIVE
                   && plan.ArchivedAt == null
                   && pageUserIds.Contains(grant.UserId)
                   && ((plan.Tier == PlanTier.FULL_ALL || plan.Tier == PlanTier.LEARN_ALL)
                       || (plan.Tier == PlanTier.COURSE && coursePlanIds.Contains(plan.Id)))
                select new GrantMeta(grant.UserId, grant.Source, grant.GrantedAt, plan.Tier, grant.Id))
            .ToListAsync(ct);

        Dictionary<Guid, GrantMeta> latestByUser = metaRows
            .GroupBy(m => m.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(m => m.GrantedAt).ThenByDescending(m => m.GrantId).First());

        // Preserve the keyset page order (GrantedAt DESC, UserId DESC).
        return pageUsers
            .Select(k => latestByUser[k.UserId])
            .Select(m => new CourseGranteeRow(m.UserId, m.Source, m.GrantedAt, m.Tier, m.GrantId))
            .ToList();
    }

    public async Task<int> CountCourseGranteesAsync(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter,
        CancellationToken ct = default) =>
        // COUNT of distinct covering users = number of groups.
        await BuildUserKeysetGrouping(courseId, authorId, userIdsFilter)
            .Select(g => g.Key)
            .CountAsync(ct);

    /// <summary>
    /// Internal flat row of a covering grant + its plan tier. Used to resolve page-level
    /// metadata in memory.
    /// </summary>
    private sealed record GrantMeta(
        Guid UserId,
        PlanGrantSource Source,
        DateTimeOffset GrantedAt,
        PlanTier Tier,
        Guid GrantId);

    /// <summary>
    /// Grouping of covering ACTIVE grants by user (global FULL/LEARN ∪ COURSE-of-course).
    /// FREE is intentionally excluded from the roster (owner decision #1: roster = real
    /// grant-holders, not free browsers). GroupBy over a single PlanGrants source
    /// (covering plan ids resolved via subquery, no inline join) translates cleanly to a
    /// PostgreSQL GROUP BY; a GroupBy over a multi-source join projection does not. Callers
    /// project the grouping to an anonymous type (Key + MAX(GrantedAt)) so the subsequent
    /// ORDER BY translates.
    /// </summary>
    private IQueryable<IGrouping<Guid, PlanGrant>> BuildUserKeysetGrouping(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter)
    {
        // COURSE plans covering this course resolve via the plan_courses join (#404 bundle).
        IQueryable<Guid> coursePlanIds = _db.PlanCourses.AsNoTracking()
            .Where(pc => pc.CourseId == courseId)
            .Select(pc => pc.PlanId);

        IQueryable<Guid> coveringPlanIds = _db.Plans.AsNoTracking()
            .Where(p => p.ArchivedAt == null
                        && ((p.Tier == PlanTier.FULL_ALL || p.Tier == PlanTier.LEARN_ALL)
                            || (p.Tier == PlanTier.COURSE && coursePlanIds.Contains(p.Id))))
            .Select(p => p.Id);

        IQueryable<PlanGrant> covering = _db.PlanGrants.AsNoTracking()
            .Where(g => g.Status == PlanGrantStatus.ACTIVE && coveringPlanIds.Contains(g.PlanId));

        if (userIdsFilter is { Count: > 0 })
        {
            covering = covering.Where(g => userIdsFilter.Contains(g.UserId));
        }

        return covering.GroupBy(g => g.UserId);
    }

    public async Task<UserGrantScope> GetUserGrantScopeAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var rows = await (
                from grant in _db.PlanGrants.AsNoTracking()
                join plan in _db.Plans.AsNoTracking() on grant.PlanId equals plan.Id
                where grant.UserId == userId
                   && grant.Status == PlanGrantStatus.ACTIVE
                   && plan.ArchivedAt == null
                select new { plan.Id, plan.Tier, plan.AuthorId })
            .ToListAsync(ct);

        // Explicit course coverage comes only from COURSE-tier plan_courses. FULL_ALL /
        // LEARN_ALL are platform-wide and expanded via ECS GetAllCourseIdsAsync.
        List<Guid> explicitCoursePlanIds = rows
            .Where(r => r.Tier == PlanTier.COURSE)
            .Select(r => r.Id)
            .Distinct()
            .ToList();

        List<Guid> explicitCourseIds = explicitCoursePlanIds.Count == 0
            ? []
            : await _db.PlanCourses.AsNoTracking()
                .Where(pc => explicitCoursePlanIds.Contains(pc.PlanId))
                .Select(pc => pc.CourseId)
                .Distinct()
                .ToListAsync(ct);

        bool hasGlobalCourseAccess = rows.Any(r => r.Tier is PlanTier.FULL_ALL or PlanTier.LEARN_ALL);

        List<Guid> freeAuthorIds = rows
            .Where(r => r.Tier == PlanTier.FREE)
            .Select(r => r.AuthorId)
            .Distinct()
            .ToList();

        return new UserGrantScope(explicitCourseIds, hasGlobalCourseAccess, freeAuthorIds);
    }
}
