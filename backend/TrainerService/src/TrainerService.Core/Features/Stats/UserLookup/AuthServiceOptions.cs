using System.ComponentModel.DataAnnotations;

namespace TrainerService.Core.Features.Stats.UserLookup;

/// <summary>
///     Config for the TrainerService → AuthService user-lookup client (epic #681). Bound from the
///     <c>AuthService</c> section. <see cref="Url"/> points directly at auth-service
///     (<c>http://auth-service:8005/</c>) — NEVER <c>http://nginx</c>, because <c>/internal/*</c>
///     endpoints aren't routed through nginx (root CLAUDE.md gotcha).
/// </summary>
public sealed class AuthServiceOptions
{
    public const string SectionName = "AuthService";

    [Required]
    public required string Url { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromMinutes(5);
}
