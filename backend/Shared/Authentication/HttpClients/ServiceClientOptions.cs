namespace PlatformAuth.HttpClients;

/// <summary>
///     Параметры OAuth2 Client Credentials для межсервисной аутентификации.
///     Привязывается к секции <c>Authentication:ServiceClient</c> в appsettings.
/// </summary>
public sealed class ServiceClientOptions
{
    /// <summary>URL эндпоинта получения токена (OpenIddict token endpoint).</summary>
    public string TokenUrl { get; init; } = string.Empty;

    /// <summary>Client ID сервисного приложения в OpenIddict.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Client Secret сервисного приложения в OpenIddict.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Проверяет, что все обязательные поля заполнены.</summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(TokenUrl)
        && !string.IsNullOrEmpty(ClientId)
        && !string.IsNullOrEmpty(ClientSecret);
}