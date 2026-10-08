using System.Collections.ObjectModel;

namespace PlatformBootstrap;

/// <summary>
/// Per-service knobs for <see cref="PlatformBootstrapExtensions.AddPlatformDefaults"/>.
/// Defaults match what every platform service uses — override only when needed.
/// </summary>
public sealed class PlatformOptions
{
    /// <summary>Scalar UI title. Defaults to the service name.</summary>
    public string? ScalarTitle { get; set; }

    /// <summary>Toggle JWT auth. True by default.</summary>
    public bool EnableJwtAuthentication { get; set; } = true;

    /// <summary>Toggle OpenAPI + Scalar UI in non-production. True by default.</summary>
    public bool EnableOpenApi { get; set; } = true;

    /// <summary>Toggle OpenTelemetry + Serilog. True by default.</summary>
    public bool EnableObservability { get; set; } = true;

    /// <summary>Toggle CORS via SachkovTech.Framework. True by default.</summary>
    public bool EnableCors { get; set; } = true;

    /// <summary>
    /// Called after JWT authentication is wired, before request logging. Use for
    /// auth-adjacent middleware (claims enrichment, per-request stamp checks, etc.).
    /// Invoked by <see cref="PlatformBootstrapExtensions.UsePlatformDefaults"/>.
    /// </summary>
    public Action<WebApplication>? AfterAuthMiddleware { get; set; }

    /// <summary>
    /// CIDR ranges, with which `X-Forwarded-For` is trusted. Anything outside the list
    /// → header dropped, client IP = the TCP peer (nginx container in our prod topology).
    ///
    /// Defaults cover Docker bridge networks (172.16.0.0/12 — covers `172.17.0.0/16`
    /// default bridge plus `172.18-31.0.0/16` for compose-created networks) and
    /// loopback (127.0.0.0/8) for local debugging. Override via
    /// `Platform:TrustedProxyNetworks` config (comma-separated CIDRs) when prod
    /// топология меняется (e.g. host-network nginx with a fixed external CIDR).
    ///
    /// Security context: agent audit 2026-05-08 (#118). До этого
    /// `KnownIPNetworks.Clear()` + `KnownProxies.Clear()` означало «доверяем любому
    /// прокси». Если порт backend-контейнера случайно открыть наружу, атакующий
    /// выставлял произвольный `X-Forwarded-For` и обходил все IP-keyed
    /// rate-limit'ы (`/connect/token`, github-webhook, OTP).
    /// </summary>
    public Collection<string> TrustedProxyNetworks { get; init; } =
    [
        "172.16.0.0/12",
        "127.0.0.0/8",
    ];
}
