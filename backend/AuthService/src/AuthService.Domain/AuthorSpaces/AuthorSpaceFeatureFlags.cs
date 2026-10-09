namespace AuthService.Domain.AuthorSpaces;

/// <summary>Feature flags пространства автора. Хранится как JSONB.</summary>
public sealed record AuthorSpaceFeatureFlags
{
    public bool GitHubIntegration { get; init; }

    public bool PrReviews { get; init; }

    public bool AiAssistant { get; init; }

    public bool Roadmaps { get; init; } = true;


    public bool CustomLanding { get; init; }
}