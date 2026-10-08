using SharedKernel.DomainEvents;

namespace TrainerService.Domain.Topics.Events;

public sealed record TopicCreatedEvent(Guid TopicId, string Slug) : IDomainEvent;

public sealed record TopicPublishedEvent(Guid TopicId) : IDomainEvent;

public sealed record TopicUnpublishedEvent(Guid TopicId) : IDomainEvent;
