using ProgressService.Core.Abstractions;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Materials.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Gamification.EventHandlers;

/// <summary>
/// Начисляет XP за просмотр материала. User-scoped: XP начисляется один раз за всё время
/// по паре (UserId, MaterialId) — идемпотентность обеспечивается ledger'ом <c>xp_awards</c>.
/// </summary>
public sealed class AwardXpOnMaterialViewed : IDomainEventHandler<MaterialViewedEvent>
{
    private readonly IXpAwardService _xpAwardService;

    public AwardXpOnMaterialViewed(IXpAwardService xpAwardService)
    {
        _xpAwardService = xpAwardService;
    }

    public async Task<UnitResult<Error>> Handle(
        MaterialViewedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        return await _xpAwardService.AwardAsync(
            new XpAwardCommand(
                UserId: domainEvent.UserId,
                EnrollmentId: null,
                AwardType: XpAwardType.MATERIAL_VIEWED,
                SourceId: domainEvent.MaterialId),
            cancellationToken);
    }
}
