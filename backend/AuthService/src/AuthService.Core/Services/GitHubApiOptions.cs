namespace AuthService.Core.Services;

public sealed class GitHubApiOptions
{
    public const string SECTION_NAME = nameof(GitHubApiOptions);

    public string Url { get; init; } = "https://api.github.com/";

    public int TimeoutSeconds { get; init; } = 7;
}
