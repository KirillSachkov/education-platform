using System.Security.Cryptography;
using AssignmentReviewService.Core.Features.Installations.Services;
using Microsoft.Extensions.Options;

namespace AssignmentReviewService.Infrastructure.GitHub;

/// <summary>
///     1.7 hardening (#264): Eager-validate <see cref="GitHubAppOptions.PrivateKeyPemBase64"/>
///     on app startup. Раньше malformed PEM проявлялся только на первом webhook /
///     install token call'е → mystery 500 в логах. Теперь — fail-fast с явным
///     сообщением.
///
///     Если App не сконфигурирован (<see cref="GitHubAppOptions.IsConfigured"/> = false) —
///     skip validation: dev/skeleton mode легитимен.
/// </summary>
internal sealed class GitHubAppOptionsValidator : IValidateOptions<GitHubAppOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubAppOptions options)
    {
        if (string.IsNullOrEmpty(options.PrivateKeyPemBase64))
        {
            // Не сконфигурирован — это валидный dev/skeleton режим. Runtime code
            // на call-site'ах сам отдаст vcs.installation_token.failed.
            return ValidateOptionsResult.Success;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(options.PrivateKeyPemBase64);
        }
        catch (FormatException ex)
        {
            return ValidateOptionsResult.Fail(
                $"GitHub App private key in {GitHubAppOptions.SECTION_NAME}:PrivateKeyPemBase64 " +
                $"is not valid base64: {ex.Message}");
        }

        string pem;
        try
        {
            pem = System.Text.Encoding.UTF8.GetString(decoded);
        }
        catch (ArgumentException ex)
        {
            return ValidateOptionsResult.Fail(
                $"GitHub App private key in {GitHubAppOptions.SECTION_NAME}:PrivateKeyPemBase64 " +
                $"is not valid UTF-8 after base64 decode: {ex.Message}");
        }

        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return ValidateOptionsResult.Fail(
                $"GitHub App private key in {GitHubAppOptions.SECTION_NAME}:PrivateKeyPemBase64 " +
                $"failed to parse as PEM RSA key: {ex.Message}");
        }

        return ValidateOptionsResult.Success;
    }
}
