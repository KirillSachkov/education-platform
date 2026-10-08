using AuthService.Core.Features.Admin.Audit;
using AuthService.Core.Features.Admin.Queries;
using AuthService.Core.Features.Auth.GitHub;
using AuthService.Core.Services;
using Core.Abstractions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHandlers(typeof(Registration).Assembly);

        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);

        services.AddHybridCache();

        services.AddScoped<TokenRevocationService>();
        services.AddScoped<GitHubCallbackHandler>();
        services.AddScoped<GitHubLinkHandler>();
        services.AddScoped<GitHubSyncHandler>();
        services.AddSingleton<GitHubEmailHandler>();
        services.AddScoped<GitHubLoginResolver>();
        services.AddScoped<GitHubProfileService>();
        services.AddScoped<GitHubOrgSyncService>();
        services.AddScoped<UsernameGenerator>();
        services.AddScoped<AdminAuditEndpointFilter>();
        services.AddScoped<ExportUsersCsvHandler>();
        services.AddSingleton<OtpAttemptLimiter>();
        services.AddSingleton<IOtpStore, RedisOtpStore>();
        services.AddSingleton<ITelegramLinkTokenStore, RedisTelegramLinkTokenStore>();

        return services;
    }
}
