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

public sealed class UnpublishPlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/unpublish", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromServices] UnpublishPlanHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new UnpublishPlanCommand(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record UnpublishPlanCommand(Guid PlanId) : ICommand;

public sealed class UnpublishPlanHandler : ICommandHandler<Guid, UnpublishPlanCommand>
{
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<UnpublishPlanHandler> _logger;

    public UnpublishPlanHandler(
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<UnpublishPlanHandler> logger)
    {
        _plans = plans;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(UnpublishPlanCommand command, CancellationToken cancellationToken)
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

        plan.Unpublish();

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation("Plan {PlanId} unpublished by {UserId}", plan.Id, _user.UserId);

        return plan.Id;
    }
}
