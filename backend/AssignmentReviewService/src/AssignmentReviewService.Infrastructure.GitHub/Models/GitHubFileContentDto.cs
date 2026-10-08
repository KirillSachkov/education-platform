using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

internal sealed record GitHubFileContentDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("encoding")] string Encoding,
    [property: JsonPropertyName("content")] string Content);
