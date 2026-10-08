using AccessService.Contracts.TrainerPro;
using AccessService.Core.Database;
using AccessService.Domain;
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

namespace AccessService.Core.Features.TrainerPro.UseCases;

/// <summary>
/// <c>POST /access/admin/trainer-pro/offer</c> — создаёт оффер-вариант подписки тренажёра (#674).
/// Под капотом — обычный <c>Plan</c> tier=SUBSCRIPTION: домен форсит OfferType=TRAINER_PRO,
/// capability TRAINER_PRO и Scope=TRAINER, поэтому вариант сразу изолирован от платформенного
/// каталога. <c>IsActive</c>=true → план публикуется (становится покупаемым через
/// <c>/access/trainer-pro/orders</c>). Требует <see cref="PlatformPermissions.Plans.MANAGE"/>.
/// </summary>
public sealed class CreateTrainerProOfferEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/admin/trainer-pro/offer", async Task<EndpointResult<Guid>> (
                [FromBody] CreateTrainerProOfferRequest request,
                [FromServices] CreateTrainerProOfferHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new CreateTrainerProOfferCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record CreateTrainerProOfferCommand(CreateTrainerProOfferRequest Request) : ICommand;

public sealed class CreateTrainerProOfferValidator : AbstractValidator<CreateTrainerProOfferCommand>
{
    public CreateTrainerProOfferValidator()
    {
        RuleFor(x => x.Request.Slug).MustBeValueObject(PlanSlug.Of);
        RuleFor(x => x.Request.DisplayName).MustBeValueObject(PlanDisplayName.Of);

        RuleFor(x => x.Request.PriceCents)
            .GreaterThan(0)
            .WithError(Error.Validation("trainer_pro.offer.price_invalid", "Цена подписки должна быть положительной"));

        RuleFor(x => x.Request.RecurringIntervalDays)
            .GreaterThan(0)
            .WithError(Error.Validation(
                "trainer_pro.offer.interval_invalid",
                "Интервал автопродления должен быть положительным числом дней"));
    }
}

public sealed class CreateTrainerProOfferHandler : ICommandHandler<Guid, CreateTrainerProOfferCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IValidator<CreateTrainerProOfferCommand> _validator;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<CreateTrainerProOfferHandler> _logger;

    public CreateTrainerProOfferHandler(
        IPlansRepository plans,
        IValidator<CreateTrainerProOfferCommand> validator,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<CreateTrainerProOfferHandler> logger)
    {
        _plans = plans;
        _validator = validator;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateTrainerProOfferCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        CreateTrainerProOfferRequest req = command.Request;
        PlanSlug slug = PlanSlug.Of(req.Slug).Value;
        PlanDisplayName displayName = PlanDisplayName.Of(req.DisplayName).Value;

        string slugValue = slug.Value;
        bool slugTaken = await _plans.ExistsAsync(p => p.Slug.Value == slugValue, cancellationToken);
        if (slugTaken)
        {
            return AccessErrors.PlanSlugConflict(slug.Value);
        }

        // SUBSCRIPTION tier → домен форсит OfferType=TRAINER_PRO, capability TRAINER_PRO, Scope=TRAINER.
        Result<Plan, Error> create = Plan.Create(
            _user.UserId,
            PlanTier.SUBSCRIPTION,
            slug,
            displayName,
            courseIds: [],
            requestedCapabilities: null,
            offerType: null,
            trialDurationDays: null,
            term: PlanTerm.Recurring(req.RecurringIntervalDays));
        if (create.IsFailure)
        {
            return create.Error;
        }

        Plan plan = create.Value;
        plan.UpdateDescription(req.ShortDescription, req.LongDescription);
        plan.UpdateFeatures(req.Features);
        plan.UpdateCover(req.CoverFileId);
        plan.UpdatePrice(req.PriceCents, req.Currency);
        plan.UpdateDisplayOrder(req.DisplayOrder ?? 0);
        plan.UpdateIsHighlighted(req.IsHighlighted);

        if (req.IsActive)
        {
            // SUBSCRIPTION — не singleton-tier, Publish() просто делает план покупаемым.
            plan.Publish();
        }

        await _plans.AddAsync(plan, cancellationToken);
        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Trainer-pro offer {PlanId} created by {UserId} (intervalDays={Interval}, active={Active})",
            plan.Id, _user.UserId, req.RecurringIntervalDays, req.IsActive);

        return plan.Id;
    }
}
