using AccessService.Contracts.Billing.Admin;
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
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Billing.UseCases.Admin;

/// <summary>
/// POST /access/admin/orders/{id}/revoke-grant — отзывает active <see cref="PlanGrant"/>,
/// привязанный к заказу (через <c>SourceRef = order.Id</c>). БЕЗ refund'а — для случаев
/// нарушения Terms / abuse / mistake.
///
/// Reason ОБЯЗАТЕЛЕН — пишется в audit + в <c>plan_grants.revoke_reason</c>.
/// Order статус НЕ меняется (для refund — отдельный endpoint).
///
/// Phase F.1.5 (issue #102).
/// </summary>
public sealed class RevokeOrderGrantEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/access/admin/orders/{id:guid}/revoke-grant", async Task<EndpointResult<Guid>> (
                Guid id,
                [FromBody] RevokeGrantRequest request,
                [FromServices] RevokeOrderGrantHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new RevokeOrderGrantCommand(id, request), ct))
            .RequireAnyRole(PlatformRoles.ADMIN);
}

public sealed record RevokeOrderGrantCommand(Guid OrderId, RevokeGrantRequest Request) : ICommand;

public sealed class RevokeOrderGrantCommandValidator : AbstractValidator<RevokeOrderGrantCommand>
{
    public RevokeOrderGrantCommandValidator()
    {
        RuleFor(x => x.Request.Reason)
            .NotEmpty()
            .WithError(Error.Validation("order.revoke.reason.required", "Reason обязателен для аудита"));

        RuleFor(x => x.Request.Reason)
            .MaximumLength(500)
            .WithError(Error.Validation("order.revoke.reason.too_long", "Reason слишком длинный (>500 символов)"));
    }
}

public sealed class RevokeOrderGrantHandler : ICommandHandler<Guid, RevokeOrderGrantCommand>
{
    private readonly IOrdersRepository _orders;
    private readonly IOrderEventsRepository _orderEvents;
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<RevokeOrderGrantCommand> _validator;
    private readonly ILogger<RevokeOrderGrantHandler> _logger;

    public RevokeOrderGrantHandler(
        IOrdersRepository orders,
        IOrderEventsRepository orderEvents,
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<RevokeOrderGrantCommand> validator,
        ILogger<RevokeOrderGrantHandler> logger)
    {
        _orders = orders;
        _orderEvents = orderEvents;
        _grants = grants;
        _plans = plans;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        RevokeOrderGrantCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Result<Order, Error> orderResult = await _orders.GetByAsync(
            o => o.Id == command.OrderId, cancellationToken);
        if (orderResult.IsFailure)
        {
            return orderResult.Error;
        }

        Order order = orderResult.Value;

        // Найти ACTIVE grant связанный с этим заказом (source_ref = order.Id).
        Guid orderRef = order.Id;
        Result<PlanGrant, Error> grantResult = await _grants.GetByAsync(
            g => g.UserId == order.UserId
                 && g.PlanId == order.PlanId
                 && g.Status == PlanGrantStatus.ACTIVE
                 && g.SourceRef == orderRef,
            cancellationToken);
        if (grantResult.IsFailure)
        {
            return AccessErrors.GrantNotFound();
        }

        PlanGrant grant = grantResult.Value;
        Result<Guid?, Error> canonicalPlanResult = await CanonicalTelegramPlanResolver.ResolveAsync(
            grant.PlanId,
            _plans,
            cancellationToken);
        if (canonicalPlanResult.IsFailure)
            return canonicalPlanResult.Error;

        UnitResult<Error> revokeResult = grant.Revoke(_user.UserId, command.Request.Reason);
        if (revokeResult.IsFailure)
        {
            return revokeResult.Error;
        }

        await _outbox.PublishAsync(new PlanGrantRevoked(
            grant.Id,
            grant.UserId,
            grant.PlanId,
            command.Request.Reason,
            grant.RevokedAt!.Value,
            canonicalPlanResult.Value));

        // Audit-row с reason.
        string escapedReason = System.Text.Json.JsonSerializer.Serialize(command.Request.Reason);
        await _orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                OrderEventType.MANUAL_REVOKE_BY_ADMIN,
                $$"""{"reason":{{escapedReason}},"grant_id":"{{grant.Id}}"}""",
                actorUserId: _user.UserId),
            cancellationToken);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Manual revoke grant {GrantId} для Order {OrderId} админом {Actor} (reason={Reason})",
            grant.Id, order.Id, _user.UserId, command.Request.Reason);

        return grant.Id;
    }
}
