using Microsoft.Extensions.Configuration;
using Scalar.AspNetCore;

namespace PlatformAuth;

/// <summary>
///     Extension для настройки Scalar UI с OAuth2 (OpenIddict).
/// </summary>
public static class ScalarExtensions
{
    private const string OAUTH2_SCHEME_KEY = "OAuth2";

    /// <summary>
    ///     Настраивает OAuth2 Authorization Code flow в Scalar для авторизации через OpenIddict.
    ///     Читает AuthorizationUrl, TokenUrl и Scalar:ClientId из секции "Authentication".
    ///     Если Scalar:ClientId не задан, fallback — Authentication:Audience.
    ///     Если AuthorizationUrl/TokenUrl не заданы, OAuth2 flow не подключается.
    /// </summary>
    /// <typeparam name="T">Тип опций (ScalarOptions).</typeparam>
    /// <param name="options">Опции Scalar.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <returns>Опции Scalar для chaining.</returns>
    public static T ConfigureOAuth2<T>(this T options, IConfiguration configuration)
        where T : ScalarOptions
    {
        IConfigurationSection authSection = configuration.GetSection("Authentication");
        string? authorizationUrl = authSection["AuthorizationUrl"];
        string? tokenUrl = authSection["TokenUrl"];
        string clientId = authSection["Scalar:ClientId"]
            ?? authSection["ScalarClientId"]
            ?? authSection["Audience"]
            ?? string.Empty;
        string? clientSecret = authSection["Scalar:ClientSecret"] ?? authSection["ScalarClientSecret"];

        if (authorizationUrl is null || tokenUrl is null || string.IsNullOrEmpty(clientId))
        {
            return options;
        }

        options
            .AddPreferredSecuritySchemes(OAUTH2_SCHEME_KEY)
            .AddAuthorizationCodeFlow(OAUTH2_SCHEME_KEY, flow =>
            {
                flow
                    .WithClientId(clientId)
                    .WithPkce(Pkce.Sha256);

                if (!string.IsNullOrWhiteSpace(clientSecret))
                {
                    flow.WithClientSecret(clientSecret);
                }
            });

        return options;
    }
}
