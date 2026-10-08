namespace AuthService.Contracts.AuthorSpaces;

public sealed record AuthorSpaceDetailResponse(
    Guid AuthorId,
    string Slug,
    string? Tagline,
    Guid? LogoAssetId,
    AuthorSpaceFeatureFlagsDto FeatureFlags,
    DateTime CreatedAt,
    DateTime UpdatedAt);
