using AccessService.Contracts.Plans.Requests;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class UpdatePlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/access/plans/{planId:guid}", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromBody] UpdatePlanRequest request,
                [FromServices] UpdatePlanHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new UpdatePlanCommand(planId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record UpdatePlanCommand(Guid PlanId, UpdatePlanRequest Request) : ICommand;

public sealed class UpdatePlanValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanValidator()
    {
        RuleFor(x => x.Request.OfferType)
            .Must(o => Enum.TryParse<PlanOfferType>(o, ignoreCase: true, out _))
            .When(x => x.Request.OfferType is not null)
            .WithError(Error.Validation("plan.offer_type.invalid", "Неизвестный формат оффера"));
        RuleFor(x => x.Request.Currency)
            .Must(currency => string.IsNullOrWhiteSpace(currency)
                || string.Equals(currency, "RUB", StringComparison.OrdinalIgnoreCase))
            .WithError(Error.Validation(
                "plan.currency.unsupported",
                "Платёжный терминал поддерживает только RUB"));
    }
}

public sealed class UpdatePlanHandler : ICommandHandler<Guid, UpdatePlanCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly IValidator<UpdatePlanCommand> _validator;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdatePlanHandler> _logger;

    public UpdatePlanHandler(
        IPlansRepository plans,
        IPlanOnboardingFlowsRepository flows,
        IValidator<UpdatePlanCommand> validator,
        ITransactionManager transactions,
        IOutboxService outbox,
        UserScopedData user,
        TimeProvider time,
        ILogger<UpdatePlanHandler> logger)
    {
        _plans = plans;
        _flows = flows;
        _validator = validator;
        _transactions = transactions;
        _outbox = outbox;
        _user = user;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

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

        if (plan.IsTrial)
        {
            return AccessErrors.PlanTrialReadOnly();
        }

        UpdatePlanRequest req = command.Request;
        HashSet<Guid> previousCourseIds = [.. plan.GetCourseIds()];
        PlanCapabilities previousCapabilities = plan.Capabilities;

        if (req.DisplayName is not null)
        {
            Result<PlanDisplayName, Error> name = PlanDisplayName.Of(req.DisplayName);
            if (name.IsFailure)
            {
                return name.Error;
            }

            plan.UpdateDisplayName(name.Value);
        }

        if (req.ShortDescription is not null || req.LongDescription is not null)
        {
            plan.UpdateDescription(
                req.ShortDescription ?? plan.ShortDescription,
                req.LongDescription ?? plan.LongDescription);
        }

        if (req.Features is not null)
        {
            plan.UpdateFeatures(req.Features);
        }

        if (req.CoverFileId is not null)
        {
            plan.UpdateCover(req.CoverFileId);
        }

        if (req.PriceCents is not null || req.Currency is not null)
        {
            plan.UpdatePrice(req.PriceCents ?? plan.PriceCents, req.Currency ?? plan.Currency);
        }

        // Bundle (#404): непустой CourseIds заменяет набор курсов COURSE-плана. null /
        // пустой = не менять. SetCourses сам валидирует tier и рейзит bound/unbound domain
        // events для diff'а пока план в каталоге.
        if (req.CourseIds is { Count: > 0 })
        {
            UnitResult<Error> setCourses = plan.SetCourses(req.CourseIds);
            if (setCourses.IsFailure)
            {
                return setCourses.Error;
            }
        }

        if (req.DisplayOrder is not null)
        {
            plan.UpdateDisplayOrder(req.DisplayOrder.Value);
        }

        if (req.Capabilities is not null)
        {
            plan.UpdateCapabilities(PlanCapabilitiesMapper.FromStrings(req.Capabilities));
        }

        // OfferType: null = не менять. Невалидная строка → 400; tier-конфликт
        // (FULL_ACCESS на COURSE-tier) отвергается доменом через UpdateOfferType.
        if (req.OfferType is not null)
        {
            if (!Enum.TryParse(req.OfferType, ignoreCase: true, out PlanOfferType offerType))
            {
                return Error.Validation("plan.offer_type.invalid", "Неизвестный формат оффера");
            }

            UnitResult<Error> offerUpdate = plan.UpdateOfferType(offerType);
            if (offerUpdate.IsFailure)
            {
                return offerUpdate.Error;
            }
        }

        if (req.IsHighlighted is not null)
        {
            plan.UpdateIsHighlighted(req.IsHighlighted.Value);
        }

        // GithubOrgSlug: null = не менять, "" = снять привязку, иное = установить.
        bool githubOrgChanged = false;
        if (req.GithubOrgSlug is not null)
        {
            string? newSlug = string.IsNullOrWhiteSpace(req.GithubOrgSlug) ? null : req.GithubOrgSlug;
            string? prevOrg = plan.GitHubOrg;
            UnitResult<Error> orgUpdate = plan.UpdateGithubOrg(newSlug);
            if (orgUpdate.IsFailure)
            {
                return orgUpdate.Error;
            }
            githubOrgChanged = !string.Equals(prevOrg, plan.GitHubOrg, StringComparison.Ordinal);
        }

        // TelegramWelcomeMessage: null = не менять, "" = очистить, иное = установить (≤4096).
        if (req.TelegramWelcomeMessage is not null)
        {
            if (req.TelegramWelcomeMessage.Length > Plan.TELEGRAM_WELCOME_MAX_LENGTH)
            {
                return Error.Validation(
                    "plan.telegram.welcome.too.long",
                    $"Приветствие не должно превышать {Plan.TELEGRAM_WELCOME_MAX_LENGTH} символов");
            }

            plan.UpdateTelegramWelcomeMessage(req.TelegramWelcomeMessage);
        }

        // Auto-sync GITHUB-step в onboarding flow если автор поменял org-привязку.
        // Запускается до SaveChanges чтобы изменения flow.Steps попали в один INSERT/UPDATE.
        if (githubOrgChanged)
        {
            await SyncOnboardingGithubStepAsync(plan.Id, plan.GitHubOrg, cancellationToken);
        }

        bool entitlementsChanged = previousCapabilities != plan.Capabilities
            || !previousCourseIds.SetEquals(plan.GetCourseIds());
        if (entitlementsChanged)
        {
            await _outbox.PublishAsync(new PlanEntitlementsChanged(
                plan.Id,
                _time.GetUtcNow()));
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation("Plan {PlanId} updated by {UserId}", plan.Id, _user.UserId);

        return plan.Id;
    }

    private async Task SyncOnboardingGithubStepAsync(Guid planId, string? newOrg, CancellationToken ct)
    {
        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(f => f.PlanId == planId, ct);
        if (flowResult.IsFailure || !flowResult.Value.IsEnabled)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        if (string.IsNullOrEmpty(newOrg))
        {
            flowResult.Value.RemoveAutoStep(PlanOnboardingStepType.GITHUB, now);
            _logger.LogInformation(
                "Auto-removed GITHUB step from onboarding flow plan={PlanId} (org unset)", planId);
        }
        else
        {
            flowResult.Value.EnsureAutoStep(PlanOnboardingStepType.GITHUB, now);
            _logger.LogInformation(
                "Auto-ensured GITHUB step in onboarding flow plan={PlanId} (org={Org})",
                planId, newOrg);
        }
    }
}
