namespace AuthService.Contracts.AuthorSpaces;

public sealed record UpdateAuthorSpaceRequest(
    string? Tagline,
    Guid? LogoAssetId,
    AuthorSpaceFeatureFlagsDto? FeatureFlags);
