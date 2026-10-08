using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Core.Database;
using AccessService.Core.Features.PlanGrants.Services;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.UseCases;

/// <summary>
/// POST /internal/access/grants/lifetime-by-author — service-to-service.
/// Issues the author's default FULL_ALL / LEARN_ALL plan-grant to a user.
///
/// Used by ProgressService GitHub auto-enrollment (Phase C #43): after the legacy
/// CourseEnrollment chain runs, ProgressService also calls this endpoint so the user
/// gets global <c>plan:all</c> access plus a legacy author lifetime alias until resync.
///
/// Idempotent: existing ACTIVE grant on the same (user, plan) is reused.
/// Plan resolution: looks up <c>(authorId, slug=default-lifetime-all, tier=FULL_ALL/LEARN_ALL)</c>.
/// If author has no default plan — returns 404. Caller is expected to handle silently.
/// </summary>
public sealed class GrantByAuthorEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/access/grants/lifetime-by-author", async Task<EndpointResult<PlanGrantDto>> (
                [FromBody] GrantByAuthorRequest request,
                [FromServices] GrantByAuthorHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GrantByAuthorCommand(request), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed record GrantByAuthorCommand(GrantByAuthorRequest Request) : ICommand;

public sealed class GrantByAuthorHandler : ICommandHandler<PlanGrantDto, GrantByAuthorCommand>
{
    public const string DEFAULT_LIFETIME_PLAN_SLUG = "default-lifetime-all";

    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<GrantByAuthorHandler> _logger;

    public GrantByAuthorHandler(
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        IOutboxService outbox,
        ITransactionManager transactions,
        ILogger<GrantByAuthorHandler> logger)
    {
        _plans = plans;
        _grants = grants;
        _outbox = outbox;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<Result<PlanGrantDto, Error>> Handle(
        GrantByAuthorCommand command,
        CancellationToken cancellationToken)
    {
        GrantByAuthorRequest req = command.Request;

        if (!Enum.TryParse<PlanGrantSource>(req.Source, out PlanGrantSource source))
        {
            return Error.Validation("grant.source.invalid", $"Неизвестный source: '{req.Source}'");
        }

        // Whitelist: only auto-grant sources are accepted on this endpoint. INVITE_LINK
        // / TRIAL / PURCHASE / MIGRATION go through their dedicated handlers.
        if (source is not (PlanGrantSource.GITHUB_ORG or PlanGrantSource.TELEGRAM_F1 or PlanGrantSource.ADMIN_GRANT))
        {
            return Error.Validation("grant.source.invalid", $"Source '{req.Source}' не поддерживается этим endpoint'ом");
        }

        Guid authorId = req.AuthorId;
        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.AuthorId == authorId
                 && (p.Tier == PlanTier.FULL_ALL || p.Tier == PlanTier.LEARN_ALL)
                 && p.Slug.Value == DEFAULT_LIFETIME_PLAN_SLUG,
            cancellationToken);
        if (getPlan.IsFailure)
        {
            return getPlan.Error;
        }

        Plan plan = getPlan.Value;
        if (plan.ArchivedAt is not null)
        {
            return AccessErrors.PlanArchived();
        }

        Guid recipientId = req.UserId;
        Guid planId = plan.Id;

        Result<PlanGrant, Error> existing = await _grants.GetByAsync(
            g => g.UserId == recipientId
                 && g.PlanId == planId
                 && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return PlanGrantMapper.MapToDto(existing.Value);
        }

        // Scope-aware dedup (#687): авто-источники не плодят бессрочный grant поверх
        // существующего покрывающего scope (например срочного «доступа на месяц»).
        if (source is PlanGrantSource.GITHUB_ORG or PlanGrantSource.TELEGRAM_F1)
        {
            IReadOnlyList<PlanGrant> activeGrants = await _grants.GetManyByAsync(
                g => g.UserId == recipientId && g.Status == PlanGrantStatus.ACTIVE,
                cancellationToken);
            Guid[] grantPlanIds = activeGrants.Select(g => g.PlanId).Distinct().ToArray();
            IReadOnlyList<Plan> grantPlans = grantPlanIds.Length > 0
                ? await _plans.GetManyByAsync(p => grantPlanIds.Contains(p.Id), cancellationToken)
                : [];
            Dictionary<Guid, Plan> plansById = grantPlans.ToDictionary(p => p.Id);
            if (GrantScopeGuard.IsAlreadyCovered(plan, activeGrants, plansById))
            {
                PlanGrant covering = activeGrants.First(g =>
                    g.PlanId == plan.Id
                    || (plansById.TryGetValue(g.PlanId, out Plan? ep)
                        && UpgradeCreditCalculator.IsScopeSubset(plan, ep)));
                _logger.LogInformation(
                    "Lifetime-by-author grant skipped for user {UserId} ({Source}) — scope already covered by grant {CoveringGrantId}",
                    recipientId, source, covering.Id);
                return PlanGrantMapper.MapToDto(covering);
            }
        }

        // Trial-план → time-limited grant (#687 Bug B). default-lifetime-all обычно
        // бессрочный (TrialDurationDays == null → expiresAt == null), но проверяем явно.
        DateTimeOffset? expiresAt = plan.IsTrial
            ? DateTimeOffset.UtcNow.AddDays(plan.TrialDurationDays!.Value)
            : null;

        PlanGrant grant = PlanGrant.Create(
            recipientId,
            plan.Id,
            source,
            sourceRef: req.SourceRef,
            expiresAt: expiresAt);

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

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Lifetime plan grant {GrantId} issued to user {UserId} on author {AuthorId}'s default plan ({Source})",
            grant.Id, recipientId, authorId, source);

        return PlanGrantMapper.MapToDto(grant);
    }
}
