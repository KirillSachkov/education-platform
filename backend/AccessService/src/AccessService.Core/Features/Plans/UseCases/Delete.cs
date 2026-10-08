using System.Data.Common;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class DeletePlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/access/plans/{planId:guid}", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromServices] DeletePlanHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new DeletePlanCommand(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record DeletePlanCommand(Guid PlanId) : ICommand;

/// <summary>
/// Полное (hard) удаление плана. Разрешено только для «пустого» плана: без заказов
/// любого статуса и без активных грантов — иначе только архив. Даже PENDING-заказ
/// сохраняется: платёжная ссылка могла быть выдана, а подтверждение прийти позже.
///
/// Ссылки защищены FK RESTRICT. Удаляем допустимые дочерние проекции явным SQL
/// в одной транзакции, затем EF удаляет план; DB CASCADE добивает
/// <c>plan_courses</c> и <c>plan_onboarding_flows</c> (+ steps).
/// Публикует <see cref="PlanHardDeleted"/> — TelegramBotService отвязывает chat-binding'и.
/// Redis-теги не трогаем: активных грантов нет, а plan-теги grant-driven и shared между планами.
/// </summary>
public sealed class DeletePlanHandler : ICommandHandler<Guid, DeletePlanCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IOrdersRepository _orders;
    private readonly IPlanGrantsRepository _grants;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _user;
    private readonly ILogger<DeletePlanHandler> _logger;

    public DeletePlanHandler(
        IPlansRepository plans,
        IOrdersRepository orders,
        IPlanGrantsRepository grants,
        ITransactionManager transactions,
        IOutboxService outbox,
        UserScopedData user,
        ILogger<DeletePlanHandler> logger)
    {
        _plans = plans;
        _orders = orders;
        _grants = grants;
        _transactions = transactions;
        _outbox = outbox;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(DeletePlanCommand command, CancellationToken cancellationToken)
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

        // Transaction + parent-row lock close the check/delete TOCTOU window. Concurrent
        // order/grant INSERT takes a FK key-share lock: either it commits first and the guard
        // sees it, or plan deletion commits first and the insert fails with FK violation.
        UnitResult<Error> txResult = await _transactions.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
        {
            return txResult.Error;
        }

        DbConnection connection = _transactions.GetDbConnection();
        int? lockedPlan = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                "SELECT 1 FROM plans WHERE id = @PlanId FOR UPDATE",
                new { PlanId = plan.Id },
                cancellationToken: cancellationToken));
        if (lockedPlan is null)
        {
            return AccessErrors.PlanNotFound();
        }

        bool hasPaidOrders = await _orders.ExistsAsync(
            o => o.PlanId == plan.Id && (o.Status == OrderStatus.PAID || o.Status == OrderStatus.REFUNDED),
            cancellationToken);
        if (hasPaidOrders)
        {
            return AccessErrors.PlanHasPaidOrders();
        }

        bool hasOrders = await _orders.ExistsAsync(o => o.PlanId == plan.Id, cancellationToken);
        if (hasOrders)
        {
            return AccessErrors.PlanHasOrders();
        }

        bool hasActiveGrants = await _grants.ExistsAsync(
            g => g.PlanId == plan.Id && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);
        if (hasActiveGrants)
        {
            return AccessErrors.PlanHasActiveGrants();
        }

        // Explicit cleanup order respects FK RESTRICT:
        // - tg_join_reminders references both plans and plan_grants;
        // - invite_redemptions: нет колонки plan_id — чистим по invite_link_id / plan_grant_id плана,
        //   обязательно ДО удаления invite_links и plan_grants (подзапросы ссылаются на них).
        // Search Path=access у соединения → имена таблиц без схемы (как в ECS DeleteMaterialHandler).
        const string cascadeSql = """
            DELETE FROM tg_join_reminders WHERE plan_id = @PlanId;

            DELETE FROM invite_redemptions
            WHERE invite_link_id IN (SELECT id FROM invite_links WHERE plan_id = @PlanId)
               OR plan_grant_id  IN (SELECT id FROM plan_grants  WHERE plan_id = @PlanId);

            DELETE FROM invite_links WHERE plan_id = @PlanId;

            DELETE FROM plan_grants WHERE plan_id = @PlanId;

            DELETE FROM user_plan_onboardings WHERE plan_id = @PlanId;

            DELETE FROM plan_pinned_materials WHERE plan_id = @PlanId;

            DELETE FROM github_org_invitations WHERE plan_id = @PlanId;
            """;
        await connection.ExecuteAsync(
            new CommandDefinition(cascadeSql, new { PlanId = plan.Id }, cancellationToken: cancellationToken));

        _plans.Remove(plan);

        await _outbox.PublishAsync(new PlanHardDeleted(plan.Id, plan.AuthorId));

        UnitResult<Error> commitResult = await _transactions.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
        {
            return commitResult.Error;
        }

        _logger.LogInformation("Plan {PlanId} hard-deleted by {UserId}", plan.Id, _user.UserId);

        return plan.Id;
    }
}
