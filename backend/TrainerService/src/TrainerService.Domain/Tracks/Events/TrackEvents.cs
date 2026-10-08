using SharedKernel.DomainEvents;

namespace TrainerService.Domain.Tracks.Events;

public sealed record TrackCreatedEvent(Guid TrackId, string Slug) : IDomainEvent;

public sealed record TrackPublishedEvent(Guid TrackId) : IDomainEvent;

public sealed record TrackUnpublishedEvent(Guid TrackId) : IDomainEvent;
