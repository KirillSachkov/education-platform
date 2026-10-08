using AccessService.Contracts.Billing.Admin;
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
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Billing.UseCases.Admin;

/// <summary>
/// POST /access/admin/orders/{id}/grant-manually — выпускает <see cref="PlanGrant"/>
/// на план заказа, БЕЗ изменения статуса <see cref="Order"/>. Используется когда
/// клиент оплатил вне нашей платёжной системы (банковский перевод, наличные),
/// но Order остался в статусе PENDING/FAILED.
///
/// Reason ОБЯЗАТЕЛЕН — пишется в <c>order_events.payload</c> + actor_user_id.
/// Идемпотентно на (order, ACTIVE grant с тем же source_ref) — повтор возвращает
/// тот же grant'.
///
/// Phase F.1.5 (issue #102).
/// </summary>
public sealed class GrantManuallyEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/access/admin/orders/{id:guid}/grant-manually", async Task<EndpointResult<GrantManuallyResponse>> (
                Guid id,
                [FromBody] GrantManuallyRequest request,
                [FromServices] GrantManuallyHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GrantManuallyCommand(id, request), ct))
            .RequireAnyRole(PlatformRoles.ADMIN);
}

public sealed record GrantManuallyCommand(Guid OrderId, GrantManuallyRequest Request) : ICommand;

public sealed class GrantManuallyCommandValidator : AbstractValidator<GrantManuallyCommand>
{
    public GrantManuallyCommandValidator()
    {
        RuleFor(x => x.Request.Reason)
            .NotEmpty()
            .WithError(Error.Validation("order.grant.reason.required", "Reason обязателен для аудита"));

        RuleFor(x => x.Request.Reason)
            .MaximumLength(500)
            .WithError(Error.Validation("order.grant.reason.too_long", "Reason слишком длинный (>500 символов)"));
    }
}

public sealed class GrantManuallyHandler : ICommandHandler<GrantManuallyResponse, GrantManuallyCommand>
{
    private readonly IOrdersRepository _orders;
    private readonly IOrderEventsRepository _orderEvents;
    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<GrantManuallyCommand> _validator;
    private readonly ILogger<GrantManuallyHandler> _logger;

    public GrantManuallyHandler(
        IOrdersRepository orders,
        IOrderEventsRepository orderEvents,
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<GrantManuallyCommand> validator,
        ILogger<GrantManuallyHandler> logger)
    {
        _orders = orders;
        _orderEvents = orderEvents;
        _plans = plans;
        _grants = grants;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<GrantManuallyResponse, Error>> Handle(
        GrantManuallyCommand command,
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

        // Idempotency: ACTIVE ADMIN_GRANT с тем же source_ref=order.Id — переиспользуем.
        // Фильтр по Source важен: на одном order'е может жить и PURCHASE-grant (если
        // позже дошёл webhook PAID) — он не должен матчиться как «idempotent hit» этого
        // ручного admin-выпуска (иначе лог соврёт, и повторный вызов вернёт чужой grant).
        Guid orderRef = order.Id;
        Result<PlanGrant, Error> existingResult = await _grants.GetByAsync(
            g => g.UserId == order.UserId
                 && g.PlanId == order.PlanId
                 && g.Status == PlanGrantStatus.ACTIVE
                 && g.Source == PlanGrantSource.ADMIN_GRANT
                 && g.SourceRef == orderRef,
            cancellationToken);
        if (existingResult.IsSuccess)
        {
            _logger.LogInformation(
                "GrantManually idempotent hit для Order {OrderId}: existing grant {GrantId}",
                order.Id, existingResult.Value.Id);
            return new GrantManuallyResponse(existingResult.Value.Id);
        }

        Result<Plan, Error> planResult = await _plans.GetByAsync(
            p => p.Id == order.PlanId, cancellationToken);
        if (planResult.IsFailure)
        {
            return planResult.Error;
        }

        Plan plan = planResult.Value;

        PlanGrant grant = PlanGrant.Create(
            order.UserId,
            plan.Id,
            PlanGrantSource.ADMIN_GRANT,
            sourceRef: order.Id);
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

        // Audit-row с reason — payload как JSON object.
        string escapedReason = System.Text.Json.JsonSerializer.Serialize(command.Request.Reason);
        await _orderEvents.AddAsync(
            OrderEvent.Record(
                order.Id,
                OrderEventType.MANUAL_GRANT_BY_ADMIN,
                $$"""{"reason":{{escapedReason}},"grant_id":"{{grant.Id}}"}""",
                actorUserId: _user.UserId),
            cancellationToken);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Manual grant {GrantId} выдан для Order {OrderId} админом {Actor} (reason={Reason})",
            grant.Id, order.Id, _user.UserId, command.Request.Reason);

        return new GrantManuallyResponse(grant.Id);
    }
}
