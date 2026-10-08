using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

internal sealed record GitHubFileDto(
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("previous_filename")] string? PreviousFilename,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("additions")] int Additions,
    [property: JsonPropertyName("deletions")] int Deletions,
    [property: JsonPropertyName("patch")] string? Patch,
    [property: JsonPropertyName("sha")] string Sha);
