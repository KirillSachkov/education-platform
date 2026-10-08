namespace Shared.Messaging.IntegrationEvents.Auth.Events;

public sealed record UserUsernameUpdated(Guid UserId, string? Username);
