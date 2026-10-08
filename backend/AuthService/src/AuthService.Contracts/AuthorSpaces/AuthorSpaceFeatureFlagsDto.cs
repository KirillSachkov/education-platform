namespace AuthService.Contracts.AuthorSpaces;

public sealed record AuthorSpaceFeatureFlagsDto(
    bool GitHubIntegration,
    bool PrReviews,
    bool AiAssistant,
    bool Roadmaps,
    bool Leaderboard,
    bool CustomLanding);
