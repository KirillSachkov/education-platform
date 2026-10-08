using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.IntegrationEvents;

/// <summary>
/// Self-consumed handler that materializes plan-grant tags directly in Redis.
/// Active tiers (см. <see cref="AccessService.Domain.PlanTier"/>):
///   • <c>LEARN_ALL</c> / <c>FULL_ALL</c> → grant <c>plan:all</c>.
///   • <c>COURSE</c> / <c>SUBSCRIPTION</c> → grant <c>plan:course:{courseId}</c> per course in scope.
/// FREE tier deprecated (#358) — никаких новых FREE grants не выпускается, бесплатный
/// доступ = system default (REGISTERED) на стороне ECS, plan-tag не нужен.
/// </summary>
public sealed class SyncContentAccessOnPlanGrantCreatedHandler
{
    private readonly IUserGrantProjection _projection;
    private readonly ILogger<SyncContentAccessOnPlanGrantCreatedHandler> _logger;

    public SyncContentAccessOnPlanGrantCreatedHandler(
        IUserGrantProjection projection,
        ILogger<SyncContentAccessOnPlanGrantCreatedHandler> logger)
    {
        _projection = projection;
        _logger = logger;
    }

    public async Task Handle(PlanGrantCreated message, CancellationToken cancellationToken)
    {
        await _projection.RecalculateAsync(message.UserId, cancellationToken);

        _logger.LogInformation(
            "PlanGrantCreated {GrantId}: authoritatively recalculated tags for user {UserId}",
            message.GrantId, message.UserId);
    }
}
