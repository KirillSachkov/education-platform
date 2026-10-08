namespace Shared.Messaging.IntegrationEvents.Auth.Events;

public sealed record UserGithubLogin(
    Guid UserId,
    string? Username,
    IReadOnlyList<string> GithubOrgs);
