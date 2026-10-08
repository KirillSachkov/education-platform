namespace TrainerService.Core.Features.Stats.UserLookup;

/// <summary>
///     User display credit resolved from AuthService for the admin trainer-stats dashboard
///     (epic #681 / #680): a display name plus a ready-to-render avatar URL (origin-relative
///     FileService content path, <c>null</c> when the user has no avatar). Internal lookup type —
///     not a wire contract; the dashboard's public shape is <c>AdminAiTopUserDto</c>.
/// </summary>
public sealed record UserLookupDto(string? DisplayName, string? AvatarUrl);
