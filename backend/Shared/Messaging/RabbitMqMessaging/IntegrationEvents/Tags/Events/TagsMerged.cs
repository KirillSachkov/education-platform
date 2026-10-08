namespace Shared.Messaging.IntegrationEvents.Tags.Events;

public sealed record TagsMerged(
    Guid CanonicalTagId,
    IReadOnlyList<Guid> AliasTagIds);
