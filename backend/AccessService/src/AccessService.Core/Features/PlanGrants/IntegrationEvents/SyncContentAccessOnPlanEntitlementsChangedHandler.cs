using AccessService.Core.Database;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.IntegrationEvents;

public sealed class SyncContentAccessOnPlanEntitlementsChangedHandler
{
    private const int BATCH_SIZE = 200;

    private readonly IPlanGrantsRepository _grants;
    private readonly IUserGrantProjection _projection;
    private readonly ILogger<SyncContentAccessOnPlanEntitlementsChangedHandler> _logger;

    public SyncContentAccessOnPlanEntitlementsChangedHandler(
        IPlanGrantsRepository grants,
        IUserGrantProjection projection,
        ILogger<SyncContentAccessOnPlanEntitlementsChangedHandler> logger)
    {
        _grants = grants;
        _projection = projection;
        _logger = logger;
    }

    public async Task Handle(PlanEntitlementsChanged message, CancellationToken cancellationToken)
    {
        Guid? afterUserId = null;
        int usersRecalculated = 0;

        while (true)
        {
            IReadOnlyList<Guid> userIds = await _grants.GetActiveUserIdsByPlanBatchAsync(
                message.PlanId,
                afterUserId,
                BATCH_SIZE,
                cancellationToken);
            if (userIds.Count == 0)
            {
                break;
            }

            await _projection.RecalculateManyAsync(userIds, cancellationToken);
            usersRecalculated += userIds.Count;
            afterUserId = userIds[^1];

            if (userIds.Count < BATCH_SIZE)
            {
                break;
            }
        }

        _logger.LogInformation(
            "PlanEntitlementsChanged for plan {PlanId}: recalculated {UserCount} user(s)",
            message.PlanId,
            usersRecalculated);
    }
}
