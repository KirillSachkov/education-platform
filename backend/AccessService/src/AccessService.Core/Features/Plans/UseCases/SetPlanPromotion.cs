using AccessService.Contracts.Plans.Requests;
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

namespace AccessService.Core.Features.Plans.UseCases;

/// <summary>
/// <c>PUT /access/plans/{planId}/promotion/</c> — устанавливает (или заменяет) акцию:
/// процентную скидку на цену плана в окне дат. Эффективная цена считается в read-time
/// и подставляется в Order при покупке. Tier-1: <c>plans.manage</c>; Tier-2: ownership.
/// </summary>
public sealed class SetPlanPromotionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/access/plans/{planId:guid}/promotion/", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromBody] SetPromotionRequest request,
                [FromServices] SetPlanPromotionHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new SetPlanPromotionCommand(planId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record SetPlanPromotionCommand(Guid PlanId, SetPromotionRequest Request) : ICommand;

public sealed class SetPlanPromotionValidator : AbstractValidator<SetPlanPromotionCommand>
{
    public SetPlanPromotionValidator()
    {
        RuleFor(x => x.Request.DiscountPercent)
            .InclusiveBetween(1, 99)
            .WithError(AccessErrors.PromotionPercentInvalid());

        RuleFor(x => x.Request)
            .Must(r => r.EndsAt > r.StartsAt)
            .WithError(AccessErrors.PromotionWindowInvalid());
    }
}

public sealed class SetPlanPromotionHandler : ICommandHandler<Guid, SetPlanPromotionCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IValidator<SetPlanPromotionCommand> _validator;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;
    private readonly ILogger<SetPlanPromotionHandler> _logger;

    public SetPlanPromotionHandler(
        IPlansRepository plans,
        IValidator<SetPlanPromotionCommand> validator,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time,
        ILogger<SetPlanPromotionHandler> logger)
    {
        _plans = plans;
        _validator = validator;
        _transactions = transactions;
        _user = user;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(SetPlanPromotionCommand command, CancellationToken cancellationToken)
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

        UnitResult<Error> set = plan.SetPromotion(
            command.Request.DiscountPercent,
            command.Request.StartsAt,
            command.Request.EndsAt,
            _time.GetUtcNow());
        if (set.IsFailure)
        {
            return set.Error;
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation(
            "Promotion set on plan {PlanId} by {UserId}: {Percent}% [{StartsAt:o}..{EndsAt:o}]",
            plan.Id, _user.UserId, command.Request.DiscountPercent, command.Request.StartsAt, command.Request.EndsAt);

        return plan.Id;
    }
}
