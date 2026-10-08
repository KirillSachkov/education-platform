using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

/// <summary>
///     Response shape для <c>GET /repos/{owner}/{repo}/compare/{base}...{head}</c> (#17).
///     Используем только <c>files</c> — тот же per-file shape, что и diff endpoint.
/// </summary>
internal sealed record GitHubCompareDto(
    [property: JsonPropertyName("files")] List<GitHubFileDto>? Files);
