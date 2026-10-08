using Common;

namespace Shared.Messaging.IntegrationEvents.Tags.Events;

public sealed record TagsAddedToEntity(
    Guid EntityId,
    EntityType EntityType,
    IReadOnlyList<Guid> TagIds);
