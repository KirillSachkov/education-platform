namespace AuthService.Core.Services;

public interface IGitHubUserService
{
    Task<Result<string?, Error>> GetLoginAsync(string accessToken, CancellationToken ct);
}
