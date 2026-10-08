namespace AuthService.Contracts.AuthorSpaces;

public sealed record AuthorSpacePublicResponse(
    Guid AuthorId,
    string Slug,
    string? DisplayName,
    string? Tagline,
    string? Specialization,
    string? AboutAsAuthor,
    Guid? AvatarId,
    Guid? LogoAssetId,
    AuthorSpaceFeatureFlagsDto FeatureFlags);
