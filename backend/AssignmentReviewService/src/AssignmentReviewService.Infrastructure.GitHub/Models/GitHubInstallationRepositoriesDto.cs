using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

internal sealed record GitHubRepositoryDto(
    [property: JsonPropertyName("full_name")] string FullName);

internal sealed record GitHubInstallationRepositoriesDto(
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("repositories")] IReadOnlyList<GitHubRepositoryDto> Repositories);
