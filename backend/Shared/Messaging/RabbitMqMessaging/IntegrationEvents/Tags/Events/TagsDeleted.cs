namespace Shared.Messaging.IntegrationEvents.Tags.Events;

public sealed record TagsDeleted(IReadOnlyList<Guid> TagIds);