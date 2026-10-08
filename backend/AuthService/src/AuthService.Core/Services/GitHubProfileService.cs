using AuthService.Core.Database;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;
using Core.Database;

namespace AuthService.Core.Services;

public sealed class GitHubProfileService(
    IProfileRepository profileRepository,
    ITransactionManager transactionManager,
    ILogger<GitHubProfileService> logger)
{
    public async Task SetGitHubUrlAsync(Guid userId, string login, CancellationToken ct)
    {
        UserProfile? profile = await profileRepository.GetByAsync(p => p.Id == userId, ct);
        if (profile is null)
        {
            logger.LogWarning("Profile not found for user {UserId} when setting GitHub URL", userId);
            return;
        }

        Result<GitHubUrl, Error> urlResult = GitHubUrl.Create($"https://github.com/{login}");
        if (urlResult.IsFailure)
        {
            logger.LogWarning("Invalid GitHub URL for login {GitHubLogin}: {Error}", login, urlResult.Error);
            return;
        }

        profile.SetGitHubUrl(urlResult.Value, DateTime.UtcNow);
        await transactionManager.SaveChangesAsync(ct);
    }
}
