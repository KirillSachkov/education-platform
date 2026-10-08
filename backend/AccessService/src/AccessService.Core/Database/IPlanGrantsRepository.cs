using System.Linq.Expressions;
using AccessService.Domain;

namespace AccessService.Core.Database;

public interface IPlanGrantsRepository
{
    Task AddAsync(PlanGrant grant, CancellationToken ct = default);

    Task<Result<PlanGrant, Error>> GetByAsync(
        Expression<Func<PlanGrant, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the active grant for a user and plan with a PostgreSQL row lock held until
    /// the surrounding transaction completes. Payment confirmation uses this before
    /// deciding that an existing entitlement makes a second grant unnecessary.
    /// </summary>
    Task<Result<PlanGrant, Error>> GetActiveForUpdateAsync(
        Guid userId,
        Guid planId,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlanGrant>> GetManyByAsync(
        Expression<Func<PlanGrant, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<PlanGrant, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetActiveUserIdsByPlanBatchAsync(
        Guid planId,
        Guid? afterUserId,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="take"/> grants matching <paramref name="predicate"/>,
    /// ordered by <c>ExpiresAt</c> ASC NULLS LAST. Used by background jobs that need a
    /// bounded sweep batch — avoids loading the unbounded result set into memory.
    /// </summary>
    Task<IReadOnlyList<PlanGrant>> GetExpiringBatchAsync(
        Expression<Func<PlanGrant, bool>> predicate,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a bounded, tracked batch of recurring grants ordered by the due charge time.
    /// The exact predicate is kept in the repository so it stays aligned with the partial
    /// PostgreSQL index used by the dunning worker.
    /// </summary>
    Task<IReadOnlyList<PlanGrant>> GetDueRecurringBatchAsync(
        DateTimeOffset now,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a bounded, tracked batch whose effective access boundary has elapsed.
    /// Dunning grace takes precedence over the paid-through date.
    /// </summary>
    Task<IReadOnlyList<PlanGrant>> GetEffectiveExpiringBatchAsync(
        DateTimeOffset cutoff,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Keyset-paginate grants of a plan ordered by <c>(GrantedAt DESC, Id DESC)</c>.
    /// Used by author-side <c>GET /access/plans/{id}/grants/</c> with cursor-based
    /// pagination. <paramref name="userIdsFilter"/> narrows results to a specific
    /// set of users (used by search mode after AuthService user-lookup).
    /// </summary>
    Task<IReadOnlyList<PlanGrant>> GetPlanGrantsKeysetAsync(
        Guid planId,
        IReadOnlyList<Guid>? userIdsFilter,
        DateTimeOffset? cursorGrantedAt,
        Guid? cursorId,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Persists an <see cref="InviteRedemption"/> audit record. Lives on this repository
    /// because redemptions are always created in tandem with a <see cref="PlanGrant"/>
    /// (one transaction; <see cref="InviteLink.Redeem"/> + <see cref="PlanGrant.CreateForInvite"/>).
    /// </summary>
    Task AddRedemptionAsync(InviteRedemption redemption, CancellationToken ct = default);

    /// <summary>
    /// Возвращает distinct user IDs с активным <see cref="PlanTier.FULL_ALL"/> /
    /// <see cref="PlanTier.LEARN_ALL"/> grant'ом. <paramref name="authorId"/> оставлен
    /// для обратной совместимости route/клиента; FULL_ALL / LEARN_ALL больше не
    /// ограничены автором.
    /// Cross-aggregate JOIN
    /// (PlanGrants × Plans) — не выражается через Expression-предикат на одном
    /// PlanGrant, поэтому отдельный method.
    ///
    /// Используется NotificationService при <c>course.created</c> для fan-out
    /// auto-subscribe: юзер с FULL_ALL/LEARN_ALL grant'ом получает доступ к новому курсу
    /// автомагически, и должен также получать уведомления о новых материалах в нём.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveLifetimeUserIdsByAuthorAsync(
        Guid authorId,
        CancellationToken ct = default);

    /// <summary>
    /// Keyset-страница distinct пользователей, чей активный grant покрывает
    /// <paramref name="courseId"/> (global FULL/LEARN ∪ COURSE-of-course). Источник
    /// "кто на курсе X" в derive-модели (epic access-derive-model, Phase 0). SQL —
    /// <c>plan_grants × plans</c> с предикатом
    /// <c>status='ACTIVE' AND plan.archived_at IS NULL AND
    /// (tier IN ('FULL_ALL','LEARN_ALL') OR (tier='COURSE' AND course_id=@courseId))</c>.
    ///
    /// FREE намеренно исключён из roster'а (owner decision #1): студенты = реальные
    /// держатели grant'а, а не free-browser'ы.
    ///
    /// DISTINCT-per-user (юзер может держать и COURSE-, и lifetime-grant на один курс).
    /// Keyset order — <c>(GrantedAt DESC, UserId DESC)</c>, где GrantedAt = latest
    /// covering-grant юзера: PostgreSQL не имеет <c>max(uuid)</c>, поэтому keyset идёт по
    /// timestamp+UserId, а не по GrantId. Соответственно <paramref name="cursorId"/> — это
    /// UserId последней строки прошлой страницы (не GrantId). Опциональный
    /// <paramref name="userIdsFilter"/> — name-search (резолвится caller'ом через
    /// AuthService). <paramref name="authorId"/> больше не участвует в FULL/LEARN
    /// покрытии и сохранён только для совместимости сигнатуры.
    /// </summary>
    Task<IReadOnlyList<CourseGranteeRow>> GetCourseGranteesKeysetAsync(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter,
        DateTimeOffset? cursorGrantedAt,
        Guid? cursorId,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Общее число distinct пользователей, чей активный grant покрывает
    /// <paramref name="courseId"/>. Тот же предикат, что и
    /// <see cref="GetCourseGranteesKeysetAsync"/> (без keyset/limit) — питает
    /// <c>totalCount</c> в page-ответе. FREE исключён.
    /// </summary>
    Task<int> CountCourseGranteesAsync(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter,
        CancellationToken ct = default);

    /// <summary>
    /// Раскладывает активные grant'ы пользователя на scope покрытия: COURSE-grant'ы →
    /// <see cref="UserGrantScope.ExplicitCourseIds"/> (по <c>plan.course_id</c>),
    /// FULL_ALL / LEARN_ALL → <see cref="UserGrantScope.HasGlobalCourseAccess"/>,
    /// legacy FREE → <see cref="UserGrantScope.FreeAuthorIds"/>.
    /// Источник "мои курсы" в derive-модели (epic access-derive-model, Phase 0):
    /// handler раскрывает global scope и legacy FREE в per-course id'ы через ECS.
    /// </summary>
    Task<UserGrantScope> GetUserGrantScopeAsync(
        Guid userId,
        CancellationToken ct = default);
}
