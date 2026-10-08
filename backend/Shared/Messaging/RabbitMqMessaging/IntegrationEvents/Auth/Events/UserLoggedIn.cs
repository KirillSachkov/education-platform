namespace Shared.Messaging.IntegrationEvents.Auth.Events;

/// <summary>
/// Публикуется AuthService после каждого успешного входа (OTP-login, password,
/// GitHub external login — не на refresh-token rotation). Consumers могут
/// подписываться на login-audit / session-lifecycle реакции.
/// </summary>
public sealed record UserLoggedIn(Guid UserId, string? Username);
