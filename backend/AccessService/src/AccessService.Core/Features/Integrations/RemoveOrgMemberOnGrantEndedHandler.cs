using AccessService.Core.Database;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Core.Features.PlanGrants.Services;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Integrations;

/// <summary>
///     #687: когда у пользователя истекает (<see cref="PlanGrantExpired"/>) или отзывается
///     (<see cref="PlanGrantRevoked"/>) grant, и в результате он больше НЕ имеет активного
///     grant'а, покрывающего scope привязанного к GitHub-org плана — исключаем его из
///     организации. Так месячный доступ, дававший членство в org'и автора, действительно
///     заканчивается: и на платформе (Redis-теги уже снял sync-handler), и в GitHub-org'и.
///
///     Слушает те же события на той же очереди <c>access.content_access.sync</c>, что и
///     <see cref="PlanGrants.IntegrationEvents.SyncContentAccessOnPlanGrantExpiredHandler"/>
///     (единственная очередь, привязанная к <c>plan_grant.expired</c>/<c>.revoked</c>).
///     Wolverine склеивает оба handler'а в одну цепочку сообщения.
///
///     Ошибки GitHub API пробрасываются в Wolverine retry/DLQ. Соседний Redis-sync
///     авторитетен и идемпотентен, поэтому повтор всего envelope безопасен. Источник
///     GitHub-логина — строки <c>github_org_invitations</c> (ACCEPTED/PENDING). Юзер,
///     попавший в org мимо invitation-flow, не имеет сохранённого логина → пропускается
///     (логируется).
///
///     Требует у GitHub App право «Organization members: Read &amp; write». Если права нет —
///     <see cref="IGitHubAppApiClient.RemoveOrgMemberAsync"/> вернёт ошибку, мы её логируем.
/// </summary>
public sealed class RemoveOrgMemberOnGrantEndedHandler
{
    private const int BATCH_SIZE = 200;
    private readonly IGithubOrgInvitationsRepository _invitations;
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IAuthorGithubInstallationsRepository _installations;
    private readonly IGitHubAppApiClient _api;
    private readonly ILogger<RemoveOrgMemberOnGrantEndedHandler> _logger;

    public RemoveOrgMemberOnGrantEndedHandler(
        IGithubOrgInvitationsRepository invitations,
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IAuthorGithubInstallationsRepository installations,
        IGitHubAppApiClient api,
        ILogger<RemoveOrgMemberOnGrantEndedHandler> logger)
    {
        _invitations = invitations;
        _grants = grants;
        _plans = plans;
        _installations = installations;
        _api = api;
        _logger = logger;
    }

    public Task Handle(PlanGrantExpired message, CancellationToken cancellationToken) =>
        RemoveDroppedOrgMembershipsAsync(message.UserId, "expired", cancellationToken);

    public Task Handle(PlanGrantRevoked message, CancellationToken cancellationToken) =>
        RemoveDroppedOrgMembershipsAsync(message.UserId, "revoked", cancellationToken);

    public Task Handle(PlanGrantRenewalRefunded message, CancellationToken cancellationToken) =>
        RemoveDroppedOrgMembershipsAsync(message.UserId, "renewal_refunded", cancellationToken);

    public async Task Handle(PlanEntitlementsChanged message, CancellationToken cancellationToken)
    {
        Result<Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == message.PlanId, cancellationToken);
        if (planResult.IsFailure
            || (planResult.Value.IsActive && planResult.Value.ArchivedAt is null))
        {
            return;
        }

        Guid? afterUserId = null;
        while (true)
        {
            IReadOnlyList<Guid> userIds = await _grants.GetActiveUserIdsByPlanBatchAsync(
                message.PlanId, afterUserId, BATCH_SIZE, cancellationToken);
            if (userIds.Count == 0)
            {
                break;
            }

            foreach (Guid userId in userIds)
            {
                await RemoveDroppedOrgMembershipsAsync(userId, "plan_disabled", cancellationToken);
            }

            afterUserId = userIds[^1];
            if (userIds.Count < BATCH_SIZE)
            {
                break;
            }
        }
    }

    private async Task RemoveDroppedOrgMembershipsAsync(Guid userId, string reason, CancellationToken ct)
    {
        IReadOnlyList<GithubOrgInvitation> memberships =
            await _invitations.GetActiveMembershipsByUserAsync(userId, ct);
        if (memberships.Count == 0)
        {
            return;
        }

        // Оставшиеся активные grant'ы пользователя + их планы — для проверки покрытия.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<PlanGrant> activeGrants = await _grants.GetManyByAsync(
            g => g.UserId == userId
              && g.Status == PlanGrantStatus.ACTIVE
              && (g.ExpiresAt == null || g.ExpiresAt > now), ct);
        Guid[] grantPlanIds = activeGrants.Select(g => g.PlanId).Distinct().ToArray();
        IReadOnlyList<Plan> grantPlans = grantPlanIds.Length > 0
            ? await _plans.GetManyByAsync(
                p => grantPlanIds.Contains(p.Id) && p.IsActive && p.ArchivedAt == null,
                ct)
            : [];
        Dictionary<Guid, Plan> plansById = grantPlans.ToDictionary(p => p.Id);
        IReadOnlyList<PlanGrant> effectiveGrants = activeGrants
            .Where(g => plansById.ContainsKey(g.PlanId))
            .ToList();

        // Один org может быть привязан к одному плану (singleton-индекс), но у юзера могут
        // быть приглашения в разные org'и — дедупим по OrgLogin, берём первую строку.
        foreach (IGrouping<string, GithubOrgInvitation> byOrg in memberships
                     .GroupBy(m => m.OrgLogin, StringComparer.Ordinal))
        {
            GithubOrgInvitation invitation = byOrg.First();
            await ProcessOrgAsync(userId, invitation, effectiveGrants, plansById, reason, ct);
        }
    }

    private async Task ProcessOrgAsync(
        Guid userId,
        GithubOrgInvitation invitation,
        IReadOnlyList<PlanGrant> activeGrants,
        Dictionary<Guid, Plan> plansById,
        string reason,
        CancellationToken ct)
    {
        // План, к org'и которого привязано членство. Если плана уже нет (hard-deleted) —
        // консервативно не трогаем (нет надёжной точки сравнения scope).
        Result<Plan, Error> getOrgPlan = await _plans.GetByAsync(p => p.Id == invitation.PlanId, ct);
        if (getOrgPlan.IsFailure)
        {
            return;
        }

        Plan orgPlan = getOrgPlan.Value;

        // Если оставшийся активный grant всё ещё покрывает scope org-плана — членство
        // законно, не исключаем.
        if (GrantScopeGuard.IsAlreadyCovered(orgPlan, activeGrants, plansById))
        {
            return;
        }

        AuthorGithubInstallation? installation =
            await _installations.GetByOrgLoginAsync(invitation.OrgLogin, ct);
        if (installation is null || !installation.IsActive)
        {
            _logger.LogInformation(
                "Org removal skipped for user {UserId} from {Org} — no active GitHub App installation",
                userId, invitation.OrgLogin);
            return;
        }

        Result<bool, Error> removed = await _api.RemoveOrgMemberAsync(
            installation.InstallationId, invitation.OrgLogin, invitation.GithubLogin, ct);

        if (removed.IsSuccess)
        {
            _logger.LogInformation(
                "Removed user {UserId} ({Login}) from GitHub org {Org} — access {Reason}, scope no longer covered",
                userId, invitation.GithubLogin, invitation.OrgLogin, reason);
        }
        else
        {
            _logger.LogError(
                "Failed to remove user {UserId} ({Login}) from GitHub org {Org}: {Error}",
                userId, invitation.GithubLogin, invitation.OrgLogin, removed.Error.GetMessage());
            throw new InvalidOperationException(
                $"GitHub org membership removal failed: {removed.Error.GetMessage()}");
        }
    }
}
