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

public sealed class ArchivePlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/plans/{planId:guid}/archive", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid planId,
                [FromServices] ArchivePlanHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new ArchivePlanCommand(planId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record ArchivePlanCommand(Guid PlanId) : ICommand;

public sealed class ArchivePlanHandler : ICommandHandler<Guid, ArchivePlanCommand>
{
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _user;
    private readonly ILogger<ArchivePlanHandler> _logger;

    public ArchivePlanHandler(
        IPlansRepository plans,
        ITransactionManager transactions,
        IOutboxService outbox,
        UserScopedData user,
        ILogger<ArchivePlanHandler> logger)
    {
        _plans = plans;
        _transactions = transactions;
        _outbox = outbox;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(ArchivePlanCommand command, CancellationToken cancellationToken)
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

        UnitResult<Error> archive = plan.Archive();
        if (archive.IsFailure)
        {
            return archive.Error;
        }

        await _outbox.PublishAsync(new PlanEntitlementsChanged(
            plan.Id,
            DateTimeOffset.UtcNow));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation("Plan {PlanId} archived by {UserId}", plan.Id, _user.UserId);

        return plan.Id;
    }
}
