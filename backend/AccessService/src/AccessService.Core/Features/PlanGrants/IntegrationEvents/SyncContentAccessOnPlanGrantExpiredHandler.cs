using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.IntegrationEvents;

/// <summary>
/// Self-consumed handler для <see cref="PlanGrantExpired"/>. Семантически равнозначен
/// <see cref="SyncContentAccessOnPlanGrantRevokedHandler"/> — на TTL-expire точно так же
/// нужно recalc'нуть полный набор тегов из оставшихся ACTIVE grants.
///
/// Раньше делал наивный SREM, который ломался при multi-grant overlap (если у юзера
/// два grant'а с тем же тегом, expire одного снимал тег, теряя доступ от второго).
/// </summary>
public sealed class SyncContentAccessOnPlanGrantExpiredHandler
{
    private readonly IUserGrantProjection _projection;
    private readonly ILogger<SyncContentAccessOnPlanGrantExpiredHandler> _logger;

    public SyncContentAccessOnPlanGrantExpiredHandler(
        IUserGrantProjection projection,
        ILogger<SyncContentAccessOnPlanGrantExpiredHandler> logger)
    {
        _projection = projection;
        _logger = logger;
    }

    public async Task Handle(PlanGrantExpired message, CancellationToken cancellationToken)
    {
        await _projection.RecalculateAsync(message.UserId, cancellationToken);

        _logger.LogInformation(
            "PlanGrantExpired {GrantId}: authoritatively recalculated tags for user {UserId}",
            message.GrantId, message.UserId);
    }
}
