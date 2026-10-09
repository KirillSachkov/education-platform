namespace AuthService.Contracts.AuthorSpaces;

public sealed record AuthorSpaceFeatureFlagsDto(
    bool GitHubIntegration,
    bool PrReviews,
    bool AiAssistant,
    bool CustomLanding);