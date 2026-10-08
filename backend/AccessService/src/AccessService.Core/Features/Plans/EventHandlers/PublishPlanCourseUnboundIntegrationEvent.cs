using AccessService.Core.Database;
using AccessService.Domain.Events;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel.DomainEvents;

namespace AccessService.Core.Features.Plans.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="PlanCourseUnboundDomainEvent"/> into the
/// integration event <see cref="PlanCourseUnbound"/> published to <c>access.events</c>.
/// Consumers should drop any cached catalog binding for the (plan, course) pair.
/// </summary>
public sealed class PublishPlanCourseUnboundIntegrationEvent
    : IDomainEventHandler<PlanCourseUnboundDomainEvent>
{
    private readonly IOutboxService _outbox;
    private readonly ILogger<PublishPlanCourseUnboundIntegrationEvent> _logger;

    public PublishPlanCourseUnboundIntegrationEvent(
        IOutboxService outbox,
        ILogger<PublishPlanCourseUnboundIntegrationEvent> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(
        PlanCourseUnboundDomainEvent domainEvent,
        CancellationToken ct)
    {
        await _outbox.PublishAsync(new PlanCourseUnbound(
            domainEvent.PlanId,
            domainEvent.CourseId));

        _logger.LogInformation(
            "Published PlanCourseUnbound: PlanId={PlanId} CourseId={CourseId}",
            domainEvent.PlanId,
            domainEvent.CourseId);

        return UnitResult.Success<Error>();
    }
}
