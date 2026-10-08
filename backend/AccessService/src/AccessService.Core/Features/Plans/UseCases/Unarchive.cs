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
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class UnarchivePlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/unarchive", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromServices] UnarchivePlanHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new UnarchivePlanCommand(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record UnarchivePlanCommand(Guid PlanId) : ICommand;

public sealed class UnarchivePlanHandler : ICommandHandler<Guid, UnarchivePlanCommand>
{
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _user;
    private readonly ILogger<UnarchivePlanHandler> _logger;

    public UnarchivePlanHandler(
        IPlansRepository plans,
        ITransactionManager transactions,
        IOutboxService outbox,
        UserScopedData user,
        ILogger<UnarchivePlanHandler> logger)
    {
        _plans = plans;
        _transactions = transactions;
        _outbox = outbox;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(UnarchivePlanCommand command, CancellationToken cancellationToken)
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

        // Bundle (#404): COURSE-tier 1:1 rule снят — несколько активных COURSE-планов могут
        // покрывать один курс. `ux_plans_course_active` index дропнут, precheck не нужен.
        bool wasArchived = plan.ArchivedAt is not null;
        plan.Unarchive();
        if (wasArchived)
        {
            await _outbox.PublishAsync(new PlanEntitlementsChanged(
                plan.Id,
                DateTimeOffset.UtcNow));
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation("Plan {PlanId} unarchived by {UserId}", plan.Id, _user.UserId);

        return plan.Id;
    }
}
