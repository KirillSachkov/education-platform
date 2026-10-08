using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class PublishPlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/publish", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromServices] PublishPlanHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new PublishPlanCommand(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record PublishPlanCommand(Guid PlanId) : ICommand;

public sealed class PublishPlanHandler : ICommandHandler<Guid, PublishPlanCommand>
{
    // DB-side guard для singleton-tier conflict — финальный гейт между параллельными
    // Publish'ами. Имя должно совпадать с `CREATE UNIQUE INDEX` в migration
    // `PlatformPlanSingletonIndexes` (2026-06-18).
    private const string PLATFORM_SINGLETON_TIER_CONSTRAINT = "uq_plans_platform_tier_singleton";

    // DB-side guard для trial-singleton conflict — один active+public trial-план
    // (FULL_ALL + trial_duration_days IS NOT NULL) на платформу.
    private const string PLATFORM_TRIAL_SINGLETON_CONSTRAINT = "uq_plans_platform_trial_singleton";

    // Публичный detail-route `/pricing/{slug}` больше не принимает authorId, поэтому
    // slug опубликованного активного плана должен быть уникален на платформе.
    private const string PLATFORM_PUBLIC_SLUG_CONSTRAINT = "uq_plans_platform_public_slug";

    // Bundle (#404): COURSE-tier 1:1 rule снят — один курс может быть в нескольких планах
    // (например базовый COURSE + bundle). `ux_plans_course_active` index дропнут миграцией
    // PlanCoursesJoinTable, COURSE-active precheck больше не нужен.

    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<PublishPlanHandler> _logger;

    public PublishPlanHandler(
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<PublishPlanHandler> logger)
    {
        _plans = plans;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(PublishPlanCommand command, CancellationToken cancellationToken)
    {
        Result<Plan, Error> get = await _plans.GetByAsync(p => p.Id == command.PlanId, cancellationToken);
        if (get.IsFailure)
        {
            return get.Error;
        }

        Plan plan = get.Value;

        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        Guid currentPlanId = plan.Id;
        string slugValue = plan.Slug.Value;

        bool slugConflict = await _plans.ExistsAsync(
            p => p.Id != currentPlanId
              && p.Slug.Value == slugValue
              && p.IsActive
              && p.IsPublic
              && p.ArchivedAt == null,
            cancellationToken);
        if (slugConflict)
        {
            return AccessErrors.PlanSlugConflict(plan.Slug.Value);
        }

        // Singleton-tier check: один LEARN_ALL / FULL_ALL active+public+not-archived
        // на платформу. DB partial unique index — финальный гейт; здесь дружелюбная
        // ошибка ДО попытки flush'а. Trial-планы исключены (они FULL_ALL, но идут
        // отдельной веткой ниже — иначе пробный конфликтовал бы с бессрочным FULL_ALL).
        if (IsSingletonTier(plan.Tier) && !plan.IsTrial)
        {
            PlanTier tier = plan.Tier;
            bool conflict = await _plans.ExistsAsync(
                p => p.Id != currentPlanId
                  && p.Tier == tier
                  && p.IsActive
                  && p.IsPublic
                  && p.ArchivedAt == null
                  && p.TrialDurationDays == null,
                cancellationToken);
            if (conflict)
            {
                return AccessErrors.PlanTierDuplicate(plan.Tier);
            }
        }
        else if (plan.IsTrial)
        {
            // Trial-singleton (#595): один active+public пробный (FULL_ALL +
            // trial_duration_days IS NOT NULL) на платформу. Отдельный partial-unique
            // index `uq_plans_platform_trial_singleton`; здесь дружелюбная ошибка ДО flush'а.
            bool conflict = await _plans.ExistsAsync(
                p => p.Id != currentPlanId
                  && p.Tier == PlanTier.FULL_ALL
                  && p.IsActive
                  && p.IsPublic
                  && p.ArchivedAt == null
                  && p.TrialDurationDays != null,
                cancellationToken);
            if (conflict)
            {
                return AccessErrors.PlanTrialDuplicate();
            }
        }

        // Bundle (#404): no COURSE-tier 1:1 precheck — a course may be sold by several plans.

        plan.Publish();

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            // Translate the partial-unique violation into a friendly singleton-tier
            // conflict error. The ExistsAsync check above closes most races, but two
            // concurrent Publish'es can both pass it; DB index then rejects one with
            // `unique.constraint.violation` — without this catch, the loser sees a
            // generic 409 «нарушение уникальности» instead of the localized message.
            // Issue #252 PROD-3.
            if (plan.IsTrial
                && save.Error.Messages.Any(m =>
                    string.Equals(m.Code, "unique.constraint.violation", StringComparison.Ordinal)
                    && string.Equals(m.InvalidField, PLATFORM_TRIAL_SINGLETON_CONSTRAINT, StringComparison.Ordinal)))
            {
                return AccessErrors.PlanTrialDuplicate();
            }
            if (IsSingletonTier(plan.Tier)
                && save.Error.Messages.Any(m =>
                    string.Equals(m.Code, "unique.constraint.violation", StringComparison.Ordinal)
                    && string.Equals(m.InvalidField, PLATFORM_SINGLETON_TIER_CONSTRAINT, StringComparison.Ordinal)))
            {
                return AccessErrors.PlanTierDuplicate(plan.Tier);
            }
            if (save.Error.Messages.Any(m =>
                    string.Equals(m.Code, "unique.constraint.violation", StringComparison.Ordinal)
                    && string.Equals(m.InvalidField, PLATFORM_PUBLIC_SLUG_CONSTRAINT, StringComparison.Ordinal)))
            {
                return AccessErrors.PlanSlugConflict(plan.Slug.Value);
            }
            return save.Error;
        }

        _logger.LogInformation("Plan {PlanId} published by {UserId}", plan.Id, _user.UserId);

        return plan.Id;
    }

    private static bool IsSingletonTier(PlanTier tier) =>
        tier is PlanTier.LEARN_ALL or PlanTier.FULL_ALL;
}
