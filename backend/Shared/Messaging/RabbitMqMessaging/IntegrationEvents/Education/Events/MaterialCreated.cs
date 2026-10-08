namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record MaterialCreated(Guid MaterialId, string AccessType, Guid AuthorId);
