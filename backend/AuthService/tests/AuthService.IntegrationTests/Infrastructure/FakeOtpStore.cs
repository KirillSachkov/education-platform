using System.Collections.Concurrent;
using System.Security.Cryptography;
using AuthService.Core.Services;

namespace AuthService.IntegrationTests.Infrastructure;

public sealed class FakeOtpStore : IOtpStore
{
    private readonly ConcurrentDictionary<string, string> _codes = new();

    public Task<string?> GenerateAndStoreAsync(string email)
    {
        string code = RandomNumberGenerator.GetInt32(100_000, 999_999)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        _codes[email.ToLowerInvariant()] = code;
        return Task.FromResult<string?>(code);
    }

    public Task<bool> VerifyAndConsumeAsync(string email, string code)
    {
        string key = email.ToLowerInvariant();

        if (!_codes.TryGetValue(key, out string? stored))
            return Task.FromResult(false);

        if (!string.Equals(stored, code, StringComparison.Ordinal))
            return Task.FromResult(false);

        _codes.TryRemove(key, out _);
        return Task.FromResult(true);
    }

    public void Clear() => _codes.Clear();
}
