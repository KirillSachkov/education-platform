using Common;

namespace Shared.Messaging.IntegrationEvents.Tags.Events;

public sealed record TagsRemovedFromEntity(
    Guid EntityId,
    EntityType EntityType,
    IReadOnlyList<Guid> TagIds);
