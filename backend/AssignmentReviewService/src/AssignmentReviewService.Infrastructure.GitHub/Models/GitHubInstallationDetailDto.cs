using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

internal sealed record GitHubAccountDto(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string Type);

internal sealed record GitHubInstallationDetailDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("account")] GitHubAccountDto Account,
    [property: JsonPropertyName("repository_selection")] string RepositorySelection);
