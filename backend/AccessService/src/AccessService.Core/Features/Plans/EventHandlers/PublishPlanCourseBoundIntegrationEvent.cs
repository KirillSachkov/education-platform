using AccessService.Core.Database;
using AccessService.Domain.Events;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel.DomainEvents;

namespace AccessService.Core.Features.Plans.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="PlanCourseBoundDomainEvent"/> into the
/// integration event <see cref="PlanCourseBound"/> published to <c>access.events</c>.
/// Consumers (EducationContentService) denormalize the COURSE-tier plan price into
/// the course catalog snapshot.
/// </summary>
public sealed class PublishPlanCourseBoundIntegrationEvent
    : IDomainEventHandler<PlanCourseBoundDomainEvent>
{
    private readonly IOutboxService _outbox;
    private readonly ILogger<PublishPlanCourseBoundIntegrationEvent> _logger;

    public PublishPlanCourseBoundIntegrationEvent(
        IOutboxService outbox,
        ILogger<PublishPlanCourseBoundIntegrationEvent> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(
        PlanCourseBoundDomainEvent domainEvent,
        CancellationToken ct)
    {
        await _outbox.PublishAsync(new PlanCourseBound(
            domainEvent.PlanId,
            domainEvent.AuthorId,
            domainEvent.CourseId,
            domainEvent.PriceCents,
            domainEvent.Currency,
            domainEvent.IsActive,
            domainEvent.IsPublic));

        _logger.LogInformation(
            "Published PlanCourseBound: PlanId={PlanId} CourseId={CourseId} PriceCents={PriceCents} IsActive={IsActive} IsPublic={IsPublic}",
            domainEvent.PlanId,
            domainEvent.CourseId,
            domainEvent.PriceCents,
            domainEvent.IsActive,
            domainEvent.IsPublic);

        return UnitResult.Success<Error>();
    }
}
