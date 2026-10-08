using OpenIddict.Abstractions;

namespace AuthService.Core.Services;

public sealed class TokenRevocationService
{
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly ILogger<TokenRevocationService> _logger;

    public TokenRevocationService(
        IOpenIddictTokenManager tokenManager,
        ILogger<TokenRevocationService> logger)
    {
        _tokenManager = tokenManager;
        _logger = logger;
    }

    public async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken ct)
    {
        string subject = userId.ToString();

        // Bulk revoke in a single store UPDATE instead of the N+1 FindBySubject +
        // per-token TryRevoke loop. Backed by the openiddict_tokens.subject index (#588).
        long count = await _tokenManager.RevokeBySubjectAsync(subject, ct);

        _logger.LogInformation("Revoked {Count} tokens for user {UserId}", count, userId);
    }
}
