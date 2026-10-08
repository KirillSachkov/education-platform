using SharedKernel.DomainEvents;

namespace ServiceName.Domain.Widgets.Events;

public sealed record WidgetCreatedEvent(Guid WidgetId, Guid OwnerId, string Name) : IDomainEvent;

public sealed record WidgetRenamedEvent(Guid WidgetId, string NewName) : IDomainEvent;
