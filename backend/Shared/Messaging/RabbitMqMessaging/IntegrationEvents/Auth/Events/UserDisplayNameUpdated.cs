namespace Shared.Messaging.IntegrationEvents.Auth.Events;

public sealed record UserDisplayNameUpdated(Guid UserId, string? DisplayName);
