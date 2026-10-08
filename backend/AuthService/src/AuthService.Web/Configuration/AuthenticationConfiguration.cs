using System.Security.Cryptography;
using AuthService.Core.Features.Auth.GitHub;
using AuthService.Core.Options;
using Microsoft.IdentityModel.Tokens;
using AuthService.Domain;
using AuthService.Infrastructure.Postgres;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace AuthService.Web.Configuration;

/// <summary>
/// Identity, OpenIddict (server + client), and cookie configuration.
/// </summary>
public static class AuthenticationConfiguration
{
    private static bool IsLocalInsecureEnvironment(IWebHostEnvironment environment)
        => environment.IsDevelopment() || environment.IsEnvironment("Docker");

    public static IServiceCollection AddIdentityServices(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        services.AddIdentity<Account, Role>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;

                options.SignIn.RequireConfirmedEmail = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.ClaimsIdentity.UserIdClaimType = OpenIddictConstants.Claims.Subject;
                options.ClaimsIdentity.UserNameClaimType = OpenIddictConstants.Claims.Name;
                options.ClaimsIdentity.EmailClaimType = OpenIddictConstants.Claims.Email;
                options.ClaimsIdentity.RoleClaimType = OpenIddictConstants.Claims.Role;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromMinutes(5));

        // AddIdentity overrides default auth scheme to Identity cookies.
        // Restore OpenIddict validation as default so API endpoints (/users/*) use Bearer tokens.
        // Identity cookies are only used explicitly by connect/authorize endpoint.
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "CrambleCookie";
            options.Cookie.HttpOnly = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(180);
            options.SlidingExpiration = true;
            options.Cookie.SecurePolicy = IsLocalInsecureEnvironment(environment)
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        return services;
    }

    public static IServiceCollection AddOpenIddict(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<AuthDbContext>()
                    .ReplaceDefaultEntities<Guid>();
            })
            .AddClient(options =>
            {
                options.AllowAuthorizationCodeFlow();

                AddSigningKeys(options, environment, configuration);

                var clientAspNetCore = options.UseAspNetCore()
                    .EnableRedirectionEndpointPassthrough()
                    .EnableErrorPassthrough();

                if (IsLocalInsecureEnvironment(environment))
                    clientAspNetCore.DisableTransportSecurityRequirement();

                options.UseSystemNetHttp();

                options.SetRedirectionEndpointUris(GitHubRoutes.OIDC_CALLBACK);

                // Fetch private GitHub emails via /user/emails API
                options.AddEventHandler(GitHubEmailHandler.Descriptor);

                GitHubOptions gitHubOptions = configuration
                    .GetSection(GitHubOptions.SECTION_NAME)
                    .Get<GitHubOptions>() ?? new GitHubOptions();

                if (gitHubOptions.IsConfigured)
                {
                    options.UseWebProviders()
                        .AddGitHub(github =>
                        {
                            github.SetClientId(gitHubOptions.ClientId)
                                .SetClientSecret(gitHubOptions.ClientSecret)
                                .SetRedirectUri(GitHubRoutes.OIDC_CALLBACK)
                                .AddScopes("user:email", "read:org");

                            github.Registration.Issuer = new Uri(GitHubRoutes.AUTHORIZATION_SERVER_ISSUER);
                        });
                }
            })
            .AddServer(options =>
            {
                string? issuer = configuration["OpenIddict:Issuer"];
                if (!string.IsNullOrWhiteSpace(issuer))
                    options.SetIssuer(new Uri(issuer));

                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow();

                options.RequireProofKeyForCodeExchange();

                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetEndSessionEndpointUris("connect/logout")
                    .SetRevocationEndpointUris("connect/revocation");

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    "roles",
                    "platform",
                    "service");

                var aspNetCoreBuilder = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();

                if (IsLocalInsecureEnvironment(environment))
                    aspNetCoreBuilder.DisableTransportSecurityRequirement();

                AddSigningKeys(options, environment, configuration);

                // Short access token lifetime сокращает окно злоупотребления после revoke/блокировки.
                // Per-request security-stamp revalidation НЕ реализована — короткий access-token
                // lifetime (5 мин) и есть основная защита. Stamp сверяется только на refresh
                // (см. Token.cs IsRefreshTokenGrantType branch).
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(5));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(180));

                // Без ротации refresh_token. NextAuth SessionProvider polling, SSR auth()
                // и axios 401-retry могут параллельно дёрнуть /connect/token одним и тем же
                // токеном из cookie. С ротацией первый вызов инвалидирует токен, остальные
                // ловят invalid_grant → RefreshTokenError → fullLogout(). Без ротации
                // refresh_token живёт 180 дней и race исчезает. Trade-off: окно
                // злоупотребления при утечке = 180 дней, но токен в HttpOnly NextAuth cookie
                // и JS его не достанет.
                options.DisableRollingRefreshTokens();

                options.DisableAccessTokenEncryption();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        // OpenIddict 7.4's GitHub metadata predates GitHub's authorization-response `iss`
        // parameter. The web-provider configuration is materialized during post-configuration,
        // so amend it afterwards while retaining exact issuer validation.
        services.PostConfigure<OpenIddict.Client.OpenIddictClientOptions>(options =>
        {
            OpenIddict.Client.OpenIddictClientRegistration? registration = options.Registrations.SingleOrDefault(
                candidate => string.Equals(
                    candidate.ProviderName,
                    GitHubRoutes.PROVIDER_NAME,
                    StringComparison.Ordinal));

            if (registration is null)
                return;

            if (registration.Configuration is null)
                throw new InvalidOperationException("GitHub OpenIddict metadata was not initialized.");

            registration.Configuration.AuthorizationResponseIssParameterSupported = true;
        });

        return services;
    }

    private static bool HasSigningKeys(IConfiguration configuration)
    {
        string? signingPem = configuration[$"{SigningKeyOptions.SECTION_NAME}:SigningKeyBase64"];
        string? encryptionPem = configuration[$"{SigningKeyOptions.SECTION_NAME}:EncryptionKeyBase64"];
        return !string.IsNullOrWhiteSpace(signingPem) && !string.IsNullOrWhiteSpace(encryptionPem);
    }

    private static void AddSigningKeys(
        OpenIddictClientBuilder builder,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        if (HasSigningKeys(configuration))
        {
            var (signingKey, encryptionKey) = LoadRsaKeys(configuration);
            builder.AddSigningKey(signingKey);
            builder.AddEncryptionKey(encryptionKey);
        }
        else if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Production signing/encryption keys are required. " +
                "Set SIGNINGKEYS__SIGNINGKEYBASE64 and SIGNINGKEYS__ENCRYPTIONKEYBASE64 environment variables " +
                "with base64-encoded PEM RSA keys.");
        }
        else
        {
            builder.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();
        }
    }

    private static void AddSigningKeys(
        OpenIddictServerBuilder builder,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        if (HasSigningKeys(configuration))
        {
            var (signingKey, encryptionKey) = LoadRsaKeys(configuration);
            builder.AddSigningKey(signingKey);
            builder.AddEncryptionKey(encryptionKey);
        }
        else if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Production signing/encryption keys are required. " +
                "Set SIGNINGKEYS__SIGNINGKEYBASE64 and SIGNINGKEYS__ENCRYPTIONKEYBASE64 environment variables " +
                "with base64-encoded PEM RSA keys.");
        }
        else
        {
            builder.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();
        }
    }

    private static (RsaSecurityKey signing, RsaSecurityKey encryption) LoadRsaKeys(
        IConfiguration configuration)
    {
        string signingPem = configuration[$"{SigningKeyOptions.SECTION_NAME}:SigningKeyBase64"]!;
        string encryptionPem = configuration[$"{SigningKeyOptions.SECTION_NAME}:EncryptionKeyBase64"]!;
        return (ImportRsaKey(signingPem), ImportRsaKey(encryptionPem));
    }

    private static RsaSecurityKey ImportRsaKey(string base64Pem)
    {
        byte[] pem = Convert.FromBase64String(base64Pem);
        RSA rsa = RSA.Create();
        rsa.ImportFromPem(System.Text.Encoding.UTF8.GetString(pem));
        return new RsaSecurityKey(rsa);
    }
}
