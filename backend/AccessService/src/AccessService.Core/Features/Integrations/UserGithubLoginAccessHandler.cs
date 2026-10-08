using AccessService.Core.Database;
using AccessService.Core.Features.PlanGrants.Services;
using AccessService.Domain;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Access.Events;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AccessService.Core.Features.Integrations;

/// <summary>
/// Слушает <see cref="UserGithubLogin"/> из <c>auth.events</c>: для каждого org из списка
/// юзера ищет активные планы с <c>github_org_slug = org</c> и идемпотентно выпускает
/// PlanGrant (Source=GITHUB_ORG). Дальнейшая цепочка (ProgressService PlanGrantCreatedHandler
/// → CourseEnrollment + Redis sync) уже работает через published <see cref="PlanGrantCreated"/>.
///
/// Заменяет ProgressService.UserGithubLoginHandler — теперь источник правды живёт на плане,
/// не на курсе.
/// </summary>
public sealed class UserGithubLoginAccessHandler
{
    // DB partial unique index `uq_plan_grants_user_plan_active (user_id, plan_id) WHERE status='ACTIVE'`.
    // Имя должно совпадать с migration; используется для идемпотентного skip'а grant-race (#115).
    private const string UNIQUE_GRANT_CONSTRAINT = "uq_plan_grants_user_plan_active";

    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<UserGithubLoginAccessHandler> _logger;

    public UserGithubLoginAccessHandler(
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        IOutboxService outbox,
        ITransactionManager transactions,
        ILogger<UserGithubLoginAccessHandler> logger)
    {
        _plans = plans;
        _grants = grants;
        _outbox = outbox;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task Handle(UserGithubLogin message, CancellationToken cancellationToken)
    {
        if (message.GithubOrgs.Count == 0)
        {
            return;
        }

        // Normalize: GitHub returns org slugs case-sensitive in API, but we store lowercase.
        IReadOnlyList<string> orgs = message.GithubOrgs
            .Select(o => o.Trim().ToLowerInvariant())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (orgs.Count == 0)
        {
            return;
        }

        IReadOnlyList<Plan> matched = await _plans.GetManyByAsync(
            p => p.GitHubOrg != null
                 && orgs.Contains(p.GitHubOrg)
                 && p.IsActive
                 && p.ArchivedAt == null,
            cancellationToken);

        if (matched.Count == 0)
        {
            _logger.LogDebug(
                "UserGithubLogin for {UserId}: no plans matching orgs [{Orgs}]",
                message.UserId, string.Join(",", orgs));
            return;
        }

        int issued = 0;
        int reused = 0;

        // Все ACTIVE grant'ы юзера (не только по matched-планам) — нужны для scope-aware
        // dedup'а (#687): месячный PURCHASE/TRIAL FULL_ALL grant живёт на ДРУГОМ плане,
        // чем бессрочный org-bound FULL_ALL, поэтому per-plan dedup его не видел и молча
        // выдавал бесплатный бессрочный grant поверх оплаченного срочного.
        IReadOnlyList<PlanGrant> activeGrants = await _grants.GetManyByAsync(
            g => g.UserId == message.UserId
                 && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);

        // Планы существующих grant'ов — для сравнения scope в GrantScopeGuard.
        Guid[] grantPlanIds = activeGrants.Select(g => g.PlanId).Distinct().ToArray();
        IReadOnlyList<Plan> grantPlans = grantPlanIds.Length > 0
            ? await _plans.GetManyByAsync(p => grantPlanIds.Contains(p.Id), cancellationToken)
            : [];
        Dictionary<Guid, Plan> plansById = grantPlans.ToDictionary(p => p.Id);

        foreach (Plan plan in matched)
        {
            // Скоуп-aware skip: если у юзера уже есть ACTIVE grant с тем же или более широким
            // scope (включая срочный «доступ на месяц») — НЕ выдаём бессрочный org-grant,
            // иначе месячный доступ молча апгрейдится до «навсегда». После истечения срочного
            // grant'а org-membership снова даст доступ при следующем логине — но к тому моменту
            // ExpiredGrantsSweeper → plan_grant.expired → RemoveOrgMemberOnGrantEndedHandler
            // уже исключит юзера из org'и (#687 AC6).
            if (GrantScopeGuard.IsAlreadyCovered(plan, activeGrants, plansById))
            {
                reused++;
                continue;
            }

            // SourceRef intentionally null — org slug audit trail lives on the Plan.
            PlanGrant grant = PlanGrant.Create(
                message.UserId,
                plan.Id,
                PlanGrantSource.GITHUB_ORG,
                sourceRef: null);

            await _grants.AddAsync(grant, cancellationToken);

            await _outbox.PublishAsync(new PlanGrantCreated(
                grant.Id,
                grant.UserId,
                plan.Id,
                plan.Tier.ToString(),
                plan.AuthorId,
                plan.FirstCourseId,
                plan.IncludesFutureContent,
                grant.Source.ToString(),
                grant.SourceRef,
                grant.GrantedAt,
                grant.ExpiresAt,
                PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
                [.. plan.Courses.Select(c => c.CourseId)],
                plan.DisplayName.Value,
                plan.OfferType.ToString()));

            issued++;
        }

        if (issued == 0)
        {
            return;
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            // Grant-race: параллельный UserGithubLogin прошёл GetByAsync до нашего INSERT'а,
            // БД отбила одну из попыток через partial unique-индекс. Идемпотентно — grant уже
            // выпущен конкурентным вызовом, ops-вмешательства не требует (раньше тут был throw
            // → message уходил в DLQ на пустом месте). Issue #115.
            if (IsUniqueGrantViolation(save.Error))
            {
                _logger.LogDebug(
                    "UserGithubLogin for {UserId}: grant race detected (unique violation), treated as idempotent",
                    message.UserId);
                return;
            }

            // Иначе — schema-мисмач / connection-issue: throw → Wolverine retry/DLQ.
            throw save.Error.ToException();
        }

        _logger.LogInformation(
            "UserGithubLogin for {UserId}: issued {Issued} new grant(s), {Reused} already-active",
            message.UserId, issued, reused);
    }

    /// <summary>
    /// Trap для race-условий на partial unique-индексе grant'а — идемпотентный skip вместо
    /// DLQ. Constraint name приезжает через <see cref="ErrorMessage.InvalidField"/> из
    /// <c>TransactionManager.SaveChangesAsync</c> (после #236). Зеркалит паттерн в
    /// <see cref="IssueAutoFreeGrantsOnUserCreatedHandler"/>.
    /// </summary>
    private static bool IsUniqueGrantViolation(Error error) =>
        error.Messages.Any(m =>
            string.Equals(m.Code, "unique.constraint.violation", StringComparison.Ordinal)
            && string.Equals(m.InvalidField, UNIQUE_GRANT_CONSTRAINT, StringComparison.Ordinal));
}
