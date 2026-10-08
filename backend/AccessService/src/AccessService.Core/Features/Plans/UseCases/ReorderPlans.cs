using AccessService.Contracts.Plans.Requests;
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

/// <summary>
///     Bulk-reorder планов. Используется в author/admin UI с drag-n-drop:
///     при каждом drop фронт собирает новый порядок и отправляет одним запросом
///     (вместо N PATCH /access/plans/{id} с UpdatePlan).
/// </summary>
public sealed class ReorderPlansEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/reorder/", async Task<EndpointResult<int>> (
                [FromBody] ReorderPlansRequest request,
                [FromServices] ReorderPlansHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new ReorderPlansCommand(request.Orders), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ReorderPlansCommand(IReadOnlyList<PlanOrderItem> Orders) : ICommand;

public sealed class ReorderPlansHandler : ICommandHandler<int, ReorderPlansCommand>
{
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;

    public ReorderPlansHandler(
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user)
    {
        _plans = plans;
        _transactions = transactions;
        _user = user;
    }

    public async Task<Result<int, Error>> Handle(ReorderPlansCommand cmd, CancellationToken ct)
    {
        if (cmd.Orders.Count == 0) return 0;

        Guid[] planIds = cmd.Orders.Select(o => o.PlanId).Distinct().ToArray();
        IReadOnlyList<Plan> plans = await _plans.GetManyByAsync(p => planIds.Contains(p.Id), ct);

        if (plans.Count != planIds.Length)
        {
            return AccessErrors.PlanNotFound();
        }

        // Ownership: все планы должны принадлежать caller'у. Owner/admin можно править
        // чужие планы (управление платформой).
        if (!_user.IsAdmin && plans.Any(p => p.AuthorId != _user.UserId))
        {
            return AccessErrors.AccessDenied();
        }

        Dictionary<Guid, int> orderById = cmd.Orders.ToDictionary(o => o.PlanId, o => o.DisplayOrder);
        foreach (Plan plan in plans)
        {
            plan.UpdateDisplayOrder(orderById[plan.Id]);
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        return plans.Count;
    }
}
