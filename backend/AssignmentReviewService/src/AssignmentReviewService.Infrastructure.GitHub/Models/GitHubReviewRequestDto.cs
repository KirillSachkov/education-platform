using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

internal sealed record GitHubReviewCommentDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("body")] string Body);

internal sealed record GitHubReviewRequestDto(
    [property: JsonPropertyName("commit_id")] string CommitId,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("comments")] IReadOnlyList<GitHubReviewCommentDto> Comments);

internal sealed record GitHubReviewResponseDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("html_url")] string HtmlUrl);

/// <summary>
///     Один inline-комментарий из ответа GET .../reviews/{id}/comments (#383).
///     <c>line</c> nullable — для устаревших (outdated) позиций GitHub возвращает
///     null и переносит реальную строку в <c>original_line</c>. Нам важно только
///     тело (<c>body</c>) — путь/строка идут в контекст справочно.
/// </summary>
internal sealed record GitHubReviewCommentResponseDto(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("line")] int? Line,
    [property: JsonPropertyName("original_line")] int? OriginalLine,
    [property: JsonPropertyName("body")] string Body);
