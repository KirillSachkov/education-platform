using System.Net;
using Framework.Cors;
using Framework.Endpoints;
using Framework.Middlewares;
using Microsoft.AspNetCore.HttpOverrides;
using Observability;
using PlatformAuth;
using PlatformAuth.Authorization;
using Scalar.AspNetCore;

namespace MaterialProcessingService.Web.Configuration;

public static class AppExtensions
{
    public static IApplicationBuilder Configure(this WebApplication app)
    {
        // Trust X-Forwarded-* только от Docker private ranges + loopback (#118). MPS не на
        // PlatformBootstrap, поэтому реплицируем дефолтный TrustedProxyNetworks инлайн — иначе
        // при случайном открытии backend-порта наружу X-Forwarded-For спуфится.
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
        };
        forwardedOptions.KnownIPNetworks.Clear();
        forwardedOptions.KnownProxies.Clear();
        foreach (string cidr in new[] { "172.16.0.0/12", "127.0.0.0/8" })
        {
            string[] parts = cidr.Split('/', 2);
            if (IPAddress.TryParse(parts[0], out IPAddress? addr) && int.TryParse(parts[1], out int prefix))
            {
                forwardedOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(addr, prefix));
            }
        }
        app.UseForwardedHeaders(forwardedOptions);

        app.ConfigureCors();
        app.UseExceptionMiddleware();
        app.UseRequestCorrelationId();
        app.UseJwtAuthentication();
        // Без этого middleware политика "ai-generation" (6/час, защита от runaway AI-spend),
        // зарегистрированная в DI, не применялась — любой автор мог запускать AI неограниченно.
        app.UseRateLimiter();
        app.UsePlatformRequestLogging();

        app.MapOpenApi().AllowAnonymousEndpoint();
        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("Material Processing Service")
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                .ConfigureOAuth2(app.Configuration);
        }).AllowAnonymousEndpoint();

        app.MapPlatformHealthEndpoints();
        app.MapEndpoints();

        return app;
    }
}
