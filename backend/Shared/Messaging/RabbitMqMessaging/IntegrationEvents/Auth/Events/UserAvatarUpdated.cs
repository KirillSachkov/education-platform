namespace Shared.Messaging.IntegrationEvents.Auth.Events;

public sealed record UserAvatarUpdated(Guid UserId, Guid? AvatarId);
