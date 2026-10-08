using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace PlatformAuth.OpenApi;

/// <summary>
///     OpenAPI document transformer, который добавляет security schemes для Scalar UI:
///     - OAuth2 Authorization Code flow — для авторизации через OpenIddict прямо из Scalar.
///     - HTTP Bearer — для ручной вставки JWT-токена.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer(
    IAuthenticationSchemeProvider authenticationSchemeProvider,
    IConfiguration configuration)
    : IOpenApiDocumentTransformer
{
    private const string OAUTH2_SCHEME_KEY = "OAuth2";
    private const string BEARER_SCHEME_KEY = "Bearer";

    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        IEnumerable<AuthenticationScheme> schemes = await authenticationSchemeProvider.GetAllSchemesAsync();

        if (schemes.All(s => !string.Equals(s.Name, "Bearer", StringComparison.Ordinal)))
        {
            return;
        }

        IConfigurationSection authSection = configuration.GetSection("Authentication");
        string? authorizationUrl = authSection["AuthorizationUrl"];
        string? tokenUrl = authSection["TokenUrl"];

        Dictionary<string, IOpenApiSecurityScheme> securitySchemes = new();
        string primaryScheme;

        if (authorizationUrl is not null && tokenUrl is not null)
        {
            securitySchemes[OAUTH2_SCHEME_KEY] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(authorizationUrl),
                        TokenUrl = new Uri(tokenUrl),
                        Scopes = new Dictionary<string, string>
                        {
                            { "openid", "OpenID Connect" },
                            { "profile", "User profile" },
                            { "email", "Email" },
                            { "roles", "User roles" }
                        }
                    }
                }
            };

            primaryScheme = OAUTH2_SCHEME_KEY;
        }
        else
        {
            primaryScheme = BEARER_SCHEME_KEY;
        }

        securitySchemes[BEARER_SCHEME_KEY] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            BearerFormat = "JWT",
            Description = "Вставьте JWT-токен вручную"
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = securitySchemes;

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(primaryScheme, document)] = []
        });
    }
}
