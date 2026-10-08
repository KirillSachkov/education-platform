using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

internal sealed record GitHubTreeEntryDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("sha")] string Sha);

internal sealed record GitHubTreeResponseDto(
    [property: JsonPropertyName("tree")] IReadOnlyList<GitHubTreeEntryDto> Tree,
    [property: JsonPropertyName("truncated")] bool Truncated);
