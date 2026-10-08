namespace AuthService.Core.Features.Connect;

/// <summary>
/// Platform-specific OIDC claims emitted by AuthService alongside the OpenIddict standard claims.
/// Centralizes magic strings used in token issuance, userinfo, and frontend JWT consumption.
/// </summary>
public static class PlatformClaims
{
    public const string DISPLAY_NAME = "display_name";

    /// <summary>Private claim — security stamp, refresh-token-only, never emitted to clients.</summary>
    public const string SECURITY_STAMP = "AspNet.Identity.SecurityStamp";
}
