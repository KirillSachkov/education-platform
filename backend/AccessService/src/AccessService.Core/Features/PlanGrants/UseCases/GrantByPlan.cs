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
/// POST /internal/access/grants/by-plan — service-to-service.
/// Issues a grant on a specific plan to a user, idempotent on (user, plan, ACTIVE).
/// Whitelisted sources: GITHUB_ORG / TELEGRAM_F1 / ADMIN_GRANT.
///
/// Used by TelegramBotService F3 reverse-claim flow (user already in chat → bot grants plan).
/// </summary>
public sealed class GrantByPlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/access/grants/by-plan", async Task<EndpointResult<PlanGrantDto>> (
                [FromBody] GrantByPlanRequest request,
                [FromServices] GrantByPlanHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GrantByPlanCommand(request), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed record GrantByPlanCommand(GrantByPlanRequest Request) : ICommand;

public sealed class GrantByPlanHandler : ICommandHandler<PlanGrantDto, GrantByPlanCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<GrantByPlanHandler> _logger;

    public GrantByPlanHandler(
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        IOutboxService outbox,
        ITransactionManager transactions,
        ILogger<GrantByPlanHandler> logger)
    {
        _plans = plans;
        _grants = grants;
        _outbox = outbox;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<Result<PlanGrantDto, Error>> Handle(
        GrantByPlanCommand command,
        CancellationToken cancellationToken)
    {
        GrantByPlanRequest req = command.Request;

        if (!Enum.TryParse<PlanGrantSource>(req.Source, out PlanGrantSource source))
        {
            return Error.Validation("grant.source.invalid", $"Неизвестный source: '{req.Source}'");
        }

        if (source is not (PlanGrantSource.GITHUB_ORG or PlanGrantSource.TELEGRAM_F1 or PlanGrantSource.ADMIN_GRANT))
        {
            return Error.Validation("grant.source.invalid", $"Source '{req.Source}' не поддерживается этим endpoint'ом");
        }

        Result<Plan, Error> getPlan = await _plans.GetByAsync(p => p.Id == req.PlanId, cancellationToken);
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
        Result<PlanGrant, Error> existing = await _grants.GetByAsync(
            g => g.UserId == recipientId
                 && g.PlanId == plan.Id
                 && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return PlanGrantMapper.MapToDto(existing.Value);
        }

        // Scope-aware dedup (#687): авто-источники (GitHub-org / Telegram-F3) НЕ выдают
        // новый (потенциально бессрочный) grant, если у юзера уже есть ACTIVE grant с тем
        // же или более широким scope — иначе срочный «доступ на месяц» молча апгрейдится
        // до «навсегда». ADMIN_GRANT — осознанное действие админа, его не блокируем.
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
                // NB: возвращаем ПОКРЫВАЮЩИЙ grant — его PlanId может отличаться от
                // запрошенного, и plan_grant.created НЕ публикуется (нового grant'а нет →
                // welcome-DM не шлётся). F3-caller (TBS) ожидает лишь success-DTO — ок.
                PlanGrant covering = activeGrants.First(g =>
                    g.PlanId == plan.Id
                    || (plansById.TryGetValue(g.PlanId, out Plan? ep)
                        && UpgradeCreditCalculator.IsScopeSubset(plan, ep)));
                _logger.LogInformation(
                    "Grant on plan {PlanId} skipped for user {UserId} ({Source}) — scope already covered by grant {CoveringGrantId}",
                    plan.Id, recipientId, source, covering.Id);
                return PlanGrantMapper.MapToDto(covering);
            }
        }

        // Trial-план (TrialDurationDays > 0) → grant time-limited, как при покупке (#687 Bug B):
        // раньше TELEGRAM_F1/GITHUB_ORG путь всегда выдавал бессрочный grant, и месячный план,
        // заявленный через TG-членство, становился вечным.
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
            "Grant {GrantId} issued to user {UserId} on plan {PlanId} ({Source})",
            grant.Id, recipientId, plan.Id, source);

        return PlanGrantMapper.MapToDto(grant);
    }
}
