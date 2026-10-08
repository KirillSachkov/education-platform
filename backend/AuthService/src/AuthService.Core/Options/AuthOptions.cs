using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using PlatformAuth.Authorization;

namespace AuthService.Core.Options;

public sealed class OpenIddictClientOptions
{
    public string ClientId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public Collection<string> PostLogoutRedirectUris { get; init; } = [];
}

public sealed class OpenIddictOptions
{
    public const string SECTION_NAME = "OpenIddict";

    public OpenIddictClientOptions EducationPlatform { get; init; } = new();
    public OpenIddictClientOptions ServiceToService { get; init; } = new();
    public OpenIddictClientOptions AdminApi { get; init; } = new();
}

public sealed class GitHubOptions
{
    public const string SECTION_NAME = "GitHub";

    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}

public sealed class TelegramLinkOptions
{
    public const string SECTION_NAME = "TelegramLinkOptions";

    /// <summary>
    ///     Username Telegram-бота без <c>@</c>, используется для построения deep-link'а
    ///     <c>https://t.me/{BotUsername}?start={token}</c>.
    /// </summary>
    public string BotUsername { get; init; } = string.Empty;
}

public sealed class DefaultAdminOptions
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Name { get; init; } = "Admin";

    /// <summary>
    /// Legacy config key. Author spaces are no longer seeded for the single-platform model.
    /// Kept so existing environments with DefaultAdmin:AuthorSpaceSlug keep binding cleanly.
    /// </summary>
    public string? AuthorSpaceSlug { get; init; }
}

public sealed class SigningKeyOptions
{
    public const string SECTION_NAME = "SigningKeys";

    public string? SigningKeyBase64 { get; init; }
    public string? EncryptionKeyBase64 { get; init; }
}

public sealed class AuthServiceOptions
{
    public const string SECTION_NAME = "AuthService";

    public string DefaultRole { get; init; } = PlatformRoles.PARTICIPANT;
    [Required]
    public string FrontendBaseUrl { get; init; } = string.Empty;
    public DefaultAdminOptions? DefaultAdmin { get; init; }
}
