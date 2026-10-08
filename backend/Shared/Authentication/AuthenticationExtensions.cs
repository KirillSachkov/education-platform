using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using PlatformAuth.OpenApi;

namespace PlatformAuth;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection authSection = configuration.GetSection("Authentication");
        string fallbackIssuer = authSection["ValidIssuer"] ?? authSection["Authority"] ?? string.Empty;
        string[] validIssuers = authSection.GetSection("ValidIssuers")
            .Get<string[]>()?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? [];

        string[] validAudiences = authSection.GetSection("ValidAudiences")
            .Get<string[]>()?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? [];

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authSection["Authority"];
                options.RequireHttpsMetadata = authSection.GetValue("RequireHttpsMetadata", false);
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = validIssuers.Length > 0 ? null : fallbackIssuer,
                    ValidIssuers = validIssuers.Length > 0 ? validIssuers : null,

                    ValidateAudience = validAudiences.Length > 0,
                    ValidAudiences = validAudiences.Length > 0 ? validAudiences : null,

                    ValidateLifetime = true,
                };
            });

        services.AddHttpContextAccessor();

        // Заполняется в UserScopedDataMiddleware (и DevAuthMiddleware в Development)
        // в начале каждого запроса.
        services.AddScoped<UserScopedData>();

        services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
        services.AddScoped<IAuthorizationHandler, RoleRequirementHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    public static IServiceCollection AddOpenApiWithAuth(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });

        return services;
    }

    public static IApplicationBuilder UseJwtAuthentication(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseMiddleware<UserScopedDataMiddleware>();

        IWebHostEnvironment env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
        IConfiguration configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        bool devAuthEnabled = configuration.GetValue("DevAuth:Enabled", false);

        bool devAuthDisabled = configuration.GetValue("DevAuth:Disabled", false);

        if (!env.IsProduction() && (env.IsDevelopment() || devAuthEnabled) && !devAuthDisabled)
        {
            app.UseMiddleware<DevAuthMiddleware>();
        }

        app.UseAuthorization();

        return app;
    }
}
