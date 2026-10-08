using System.Collections.Concurrent;
using System.Security.Cryptography;
using AuthService.Core.Services;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory stand-in for <see cref="ITelegramLinkTokenStore"/> that mimics the
/// Redis implementation — single-use tokens, no TTL (tests run in milliseconds).
/// </summary>
public sealed class FakeTelegramLinkTokenStore : ITelegramLinkTokenStore
{
    private readonly ConcurrentDictionary<string, Guid> _tokens = new(StringComparer.Ordinal);

    public Task<string?> GenerateAsync(Guid userId)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        string token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        _tokens[token] = userId;
        return Task.FromResult<string?>(token);
    }

    public Task<Guid?> PeekAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult<Guid?>(null);

        if (_tokens.TryGetValue(token, out Guid userId))
            return Task.FromResult<Guid?>(userId);

        return Task.FromResult<Guid?>(null);
    }

    public Task DeleteAsync(string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            _tokens.TryRemove(token, out _);

        return Task.CompletedTask;
    }

    /// <summary>Test-only helper: seed a pre-existing token without going through GenerateAsync.</summary>
    public void SeedToken(string token, Guid userId) => _tokens[token] = userId;

    /// <summary>Test-only helper: check whether a token is still present.</summary>
    public bool Contains(string token) => _tokens.ContainsKey(token);

    public void Clear() => _tokens.Clear();
}
