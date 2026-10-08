using AccessService.Contracts.Plans.Requests;
using AccessService.Core.Database;
using AccessService.Core.Features.Plans;
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
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class CreatePlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/", async Task<EndpointResult<Guid>> (
                [FromBody] CreatePlanRequest request,
                [FromServices] CreatePlanHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new CreatePlanCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record CreatePlanCommand(CreatePlanRequest Request) : ICommand;

public sealed class CreatePlanValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanValidator()
    {
        RuleFor(x => x.Request.Tier)
            .NotEmpty()
            .Must(t => Enum.TryParse<PlanTier>(t, out _))
            .WithError(Error.Validation("plan.tier.invalid", "Неизвестный тип плана"));

        RuleFor(x => x.Request.OfferType)
            .Must(o => Enum.TryParse<PlanOfferType>(o, ignoreCase: true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Request.OfferType))
            .WithError(Error.Validation("plan.offer_type.invalid", "Неизвестный формат оффера"));

        RuleFor(x => x.Request.Slug).MustBeValueObject(PlanSlug.Of);
        RuleFor(x => x.Request.DisplayName).MustBeValueObject(PlanDisplayName.Of);
        RuleFor(x => x.Request.Currency)
            .Must(currency => string.IsNullOrWhiteSpace(currency)
                || string.Equals(currency, "RUB", StringComparison.OrdinalIgnoreCase))
            .WithError(Error.Validation(
                "plan.currency.unsupported",
                "Платёжный терминал поддерживает только RUB"));
    }
}

public sealed class CreatePlanHandler : ICommandHandler<Guid, CreatePlanCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IValidator<CreatePlanCommand> _validator;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly AccessOptions _accessOptions;
    private readonly ILogger<CreatePlanHandler> _logger;

    public CreatePlanHandler(
        IPlansRepository plans,
        IValidator<CreatePlanCommand> validator,
        ITransactionManager transactions,
        UserScopedData user,
        IOptions<AccessOptions> accessOptions,
        ILogger<CreatePlanHandler> logger)
    {
        _plans = plans;
        _validator = validator;
        _transactions = transactions;
        _user = user;
        _accessOptions = accessOptions.Value;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(CreatePlanCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        PlanTier tier = Enum.Parse<PlanTier>(command.Request.Tier);
        PlanSlug slug = PlanSlug.Of(command.Request.Slug).Value;
        PlanDisplayName displayName = PlanDisplayName.Of(command.Request.DisplayName).Value;

        // OfferType опционален: null/пусто → домен дефолтит по tier'у. Валидатор уже
        // отверг невалидную строку, поэтому parse безопасен.
        PlanOfferType? offerType = string.IsNullOrWhiteSpace(command.Request.OfferType)
            ? null
            : Enum.Parse<PlanOfferType>(command.Request.OfferType, ignoreCase: true);

        // Пробный план (#595): срок задаётся платформой (config), автор не вводит.
        // Tier форсим в FULL_ALL и offer — FULL_ACCESS (не доверяем Request.Tier для trial).
        // Для обычных планов author-controlled дни не используются — trialDurationDays=null.
        int? trialDurationDays = null;
        if (command.Request.IsTrial)
        {
            tier = PlanTier.FULL_ALL;
            offerType = PlanOfferType.FULL_ACCESS;
            trialDurationDays = _accessOptions.TrialDurationDays;
        }

        Guid authorId = _user.UserId;
        string slugValue = slug.Value;

        bool slugTaken = await _plans.ExistsAsync(
            p => p.Slug.Value == slugValue,
            cancellationToken);

        if (slugTaken)
        {
            return AccessErrors.PlanSlugConflict(slug.Value);
        }

        // Singleton-tier check happens on Publish, not Create — план создаётся как
        // draft (IsPublic=false), DB partial-unique index не задевается. См. Publish.cs.

        // Подписка (#614): периодический срок из RecurringIntervalDays. Для прочих тиров —
        // Lifetime (домен использует его как дефолт при term=null). Валидацию интервала (> 0)
        // и «SUBSCRIPTION обязан быть recurring» делает Plan.Create (SubscriptionRequiresRecurringTerm).
        PlanTerm? term = tier == PlanTier.SUBSCRIPTION
            ? PlanTerm.Recurring(command.Request.RecurringIntervalDays ?? 0)
            : null;

        Result<Plan, Error> create = Plan.Create(
            authorId,
            tier,
            slug,
            displayName,
            command.Request.CourseIds ?? [],
            command.Request.Capabilities,
            offerType,
            trialDurationDays,
            term);
        if (create.IsFailure)
        {
            return create.Error;
        }

        Plan plan = create.Value;
        plan.UpdateDescription(command.Request.ShortDescription, command.Request.LongDescription);
        plan.UpdateFeatures(command.Request.Features);
        plan.UpdateCover(command.Request.CoverFileId);
        plan.UpdatePrice(command.Request.PriceCents, command.Request.Currency);
        plan.UpdateDisplayOrder(command.Request.DisplayOrder ?? 0);
        plan.UpdateIsHighlighted(command.Request.IsHighlighted);

        await _plans.AddAsync(plan, cancellationToken);
        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation("Plan {PlanId} created by author {AuthorId} (tier={Tier})", plan.Id, authorId, tier);

        return plan.Id;
    }
}
