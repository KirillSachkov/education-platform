using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

internal sealed record GitHubUserDto(
    [property: JsonPropertyName("login")] string Login);

internal sealed record GitHubRefDto(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("sha")] string Sha);

internal sealed record GitHubPullDto(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("user")] GitHubUserDto User,
    [property: JsonPropertyName("head")] GitHubRefDto Head,
    [property: JsonPropertyName("base")] GitHubRefDto Base,
    [property: JsonPropertyName("draft")] bool Draft = false);
