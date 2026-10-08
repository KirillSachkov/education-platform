using System.Net;
using Framework.Cors;
using Framework.Middlewares;
using Observability;
using PlatformAuth;
using PlatformAuth.Authorization;
using Scalar.AspNetCore;

namespace PlatformBootstrap;

/// <summary>
/// One-call bootstrap for a platform microservice. Wires Serilog + OpenTelemetry,
/// CORS, JWT auth, OpenAPI + Scalar UI, and SachkovTech.Framework endpoint
/// discovery. Mirrors the pipeline in backend/AuthService/.../AppExtensions.cs.
/// </summary>
/// <example>
/// <code>
/// WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
/// builder.AddPlatformDefaults("OrdersService");
/// // service-specific registrations go here: AddDbContext, AddWolverine, etc.
/// WebApplication app = builder.Build();
/// app.UsePlatformDefaults();
/// app.MapEndpoints();
/// await app.RunAsync();
/// </code>
/// </example>
public static class PlatformBootstrapExtensions
{
    public static WebApplicationBuilder AddPlatformDefaults(
        this WebApplicationBuilder builder,
        string serviceName,
        Action<PlatformOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var options = new PlatformOptions();
        configure?.Invoke(options);

        string environment = builder.Environment.EnvironmentName;

        // Configuration sources: Serilog json + env-specific appsettings + env vars
        builder.Configuration
            .AddSerilogConfiguration(environment)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        if (options.EnableObservability)
        {
            builder.Services
                .AddSerilogLogging(builder.Configuration, serviceName)
                .AddObservability(builder.Configuration, serviceName);
        }

        if (options.EnableCors)
        {
            builder.Services
                .AddFrameworkCors(builder.Configuration)
                .AddCors();
        }

        if (options.EnableJwtAuthentication)
        {
            builder.Services.AddJwtAuthentication(builder.Configuration);
        }

        if (options.EnableOpenApi)
        {
            builder.Services.AddOpenApiWithAuth();
        }

        // Rate limiter: always register the DI container so UseRateLimiter in the
        // pipeline never throws. Services that want named policies call their own
        // Add{X}RateLimiting() extension afterwards — AddRateLimiter is idempotent,
        // a second call adds policies to the same container.
        builder.Services.AddRateLimiter(_ => { });

        // Endpoint discovery — call services.AddEndpoints(typeof(Core.Registration).Assembly)
        // explicitly in your Add{Service}Registrations. Endpoints live in the Core layer,
        // not the Web entry assembly, so auto-registration is intentionally NOT done here.

        // Stash the configured options in DI so UsePlatformDefaults can read them without
        // re-parsing the Options pattern.
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new PlatformServiceDescriptor(serviceName));

        return builder;
    }

    /// <summary>
    /// Standard middleware pipeline. Order: forwarded-headers → CORS → exception →
    /// correlation ID → auth (+authorization) → rate limiter → request logging →
    /// OpenAPI → health → endpoints.
    ///
    /// Rate limiter must run AFTER auth so partition factories (e.g. NotificationService
    /// PREFERENCES/BROADCAST) read the authenticated user. Before auth, all callers
    /// share an anonymous/IP partition — fine for IP-based policies (login/OTP/anon-read)
    /// but per-user buckets collapse onto IP, and anonymous 401-rejected calls still
    /// consume permits.
    /// </summary>
    public static WebApplication UsePlatformDefaults(this WebApplication app)
    {
        PlatformOptions options = app.Services.GetRequiredService<PlatformOptions>();
        PlatformServiceDescriptor descriptor = app.Services.GetRequiredService<PlatformServiceDescriptor>();

        // Trust X-Forwarded-* только от заданных CIDR-сетей (по умолчанию — Docker
        // private ranges + loopback). Если backend-порт случайно открыть наружу,
        // запрос от не-trusted hop'а проигнорирует header и client IP = TCP peer.
        // См. PlatformOptions.TrustedProxyNetworks для security context (#118).
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
        };
        forwardedOptions.KnownIPNetworks.Clear();
        forwardedOptions.KnownProxies.Clear();
        foreach (string cidr in options.TrustedProxyNetworks)
        {
            string[] parts = cidr.Split('/', 2);
            if (parts.Length == 2
                && IPAddress.TryParse(parts[0], out IPAddress? addr)
                && int.TryParse(parts[1], out int prefix))
            {
                forwardedOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(addr, prefix));
            }
        }
        app.UseForwardedHeaders(forwardedOptions);

        if (options.EnableCors)
        {
            app.ConfigureCors();
        }

        app.UseExceptionMiddleware();
        app.UseRequestCorrelationId();

        if (options.EnableJwtAuthentication)
        {
            app.UseJwtAuthentication();
            options.AfterAuthMiddleware?.Invoke(app);
        }

        app.UseRateLimiter();

        if (options.EnableObservability)
        {
            app.UsePlatformRequestLogging();
        }

        if (options.EnableOpenApi && !app.Environment.IsProduction())
        {
            app.MapOpenApi().AllowAnonymousEndpoint();
            app.MapScalarApiReference(scalar =>
            {
                scalar
                    .WithTitle(options.ScalarTitle ?? descriptor.Name)
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
            }).AllowAnonymousEndpoint();
        }

        app.MapPlatformHealthEndpoints();

        return app;
    }
}

internal sealed record PlatformServiceDescriptor(string Name);
