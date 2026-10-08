using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.IntegrationEvents;

/// <summary>
/// Self-consumed handler для <see cref="PlanGrantRevoked"/>. Atomically заменяет
/// набор тегов пользователя на union тегов всех его оставшихся ACTIVE grants —
/// чтобы revoke не задел теги, всё ещё покрытые другими grants (multi-grant overlap).
///
/// Пример: у юзера два FULL_ALL plana. Revoke одного не должен снимать
/// <c>plan:all</c> — второй plan ещё активен.
///
/// Сценарий пустого результата (нет ACTIVE grants после revoke) → ReplaceAsync
/// чистит set полностью.
/// </summary>
public sealed class SyncContentAccessOnPlanGrantRevokedHandler
{
    private readonly IUserGrantProjection _projection;
    private readonly ILogger<SyncContentAccessOnPlanGrantRevokedHandler> _logger;

    public SyncContentAccessOnPlanGrantRevokedHandler(
        IUserGrantProjection projection,
        ILogger<SyncContentAccessOnPlanGrantRevokedHandler> logger)
    {
        _projection = projection;
        _logger = logger;
    }

    public async Task Handle(PlanGrantRevoked message, CancellationToken cancellationToken)
    {
        await _projection.RecalculateAsync(message.UserId, cancellationToken);

        _logger.LogInformation(
            "PlanGrantRevoked {GrantId}: authoritatively recalculated tags for user {UserId}",
            message.GrantId, message.UserId);
    }

    public async Task Handle(PlanGrantRenewalRefunded message, CancellationToken cancellationToken)
    {
        await _projection.RecalculateAsync(message.UserId, cancellationToken);
        _logger.LogInformation(
            "PlanGrantRenewalRefunded {GrantId}: authoritatively recalculated tags for user {UserId}",
            message.GrantId,
            message.UserId);
    }
}
