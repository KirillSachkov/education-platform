namespace Shared.Messaging.IntegrationEvents.Auth.Events;

public sealed record UserCreated(Guid UserId, string? Username, string? DisplayName = null);
