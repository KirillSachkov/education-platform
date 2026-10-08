using AccessService.Contracts.TrainerPro;
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

namespace AccessService.Core.Features.TrainerPro.UseCases;

/// <summary>
/// <c>PATCH /access/admin/trainer-pro/offer/{planId}</c> — правит цену / маркетинг-поля и
/// переключает покупаемость (<c>IsActive</c> → publish/unpublish) оффер-варианта тренажёра (#674).
/// Применяется ТОЛЬКО к плану Scope=TRAINER (иначе <c>trainer_pro.offer.scope_mismatch</c>) —
/// нельзя случайно отредактировать платформенный план через trainer-API. Требует
/// <see cref="PlatformPermissions.Plans.MANAGE"/> + ownership.
/// </summary>
public sealed class UpdateTrainerProOfferEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/access/admin/trainer-pro/offer/{planId:guid}", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromBody] UpdateTrainerProOfferRequest request,
                [FromServices] UpdateTrainerProOfferHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new UpdateTrainerProOfferCommand(planId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record UpdateTrainerProOfferCommand(Guid PlanId, UpdateTrainerProOfferRequest Request) : ICommand;

public sealed class UpdateTrainerProOfferHandler : ICommandHandler<Guid, UpdateTrainerProOfferCommand>
{
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<UpdateTrainerProOfferHandler> _logger;

    public UpdateTrainerProOfferHandler(
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<UpdateTrainerProOfferHandler> logger)
    {
        _plans = plans;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateTrainerProOfferCommand command,
        CancellationToken cancellationToken)
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

        // Scope guard (#674): trainer-API правит только trainer-офферы.
        if (plan.Scope != PlanScope.TRAINER)
        {
            return AccessErrors.TrainerProOfferScopeMismatch();
        }

        UpdateTrainerProOfferRequest req = command.Request;

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

        if (req.DisplayOrder is not null)
        {
            plan.UpdateDisplayOrder(req.DisplayOrder.Value);
        }

        if (req.IsHighlighted is not null)
        {
            plan.UpdateIsHighlighted(req.IsHighlighted.Value);
        }

        // IsActive переключает покупаемость через publish/unpublish (не архив — обратимо).
        if (req.IsActive is not null)
        {
            if (req.IsActive.Value)
            {
                plan.Publish();
            }
            else
            {
                plan.Unpublish();
            }
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation("Trainer-pro offer {PlanId} updated by {UserId}", plan.Id, _user.UserId);

        return plan.Id;
    }
}
