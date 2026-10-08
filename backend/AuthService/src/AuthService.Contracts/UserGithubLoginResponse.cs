namespace AuthService.Contracts;

public sealed record UserGithubLoginResponse(Guid UserId, string? GithubLogin);
