namespace EducationContentService.Core.Features.AuthorCredit;

/// <summary>
///     Config for the ECS-local AuthService author-lookup client (issue #569).
///     Bound from the <c>AuthService</c> section. <see cref="Url"/> points directly at
///     auth-service (<c>http://auth-service:8005/</c>) — NEVER <c>http://nginx</c>,
///     because <c>/internal/*</c> endpoints aren't routed through nginx (root CLAUDE.md gotcha).
/// </summary>
public sealed class AuthServiceOptions
{
    public const string SectionName = "AuthService";

    public required string Url { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromMinutes(5);
}
