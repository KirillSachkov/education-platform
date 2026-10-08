namespace Shared.Messaging.IntegrationEvents.Tags.Events;

public sealed record TagAliasRemoved(
    Guid CanonicalTagId,
    IReadOnlyList<Guid> RemovedAliasTagIds);
