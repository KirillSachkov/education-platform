namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record EntityHardDeleted(Guid EntityId, string EntityType);
