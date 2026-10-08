using System.Text.Json.Serialization;

namespace AssignmentReviewService.Infrastructure.GitHub.Models;

/// <summary>
///     Тело POST'а одиночного коммента/reply автора в PR (#713). GitHub принимает
///     единственное поле <c>body</c> и для reply-на-review-коммент
///     (<c>.../pulls/{n}/comments/{id}/replies</c>), и для нового issue-коммента
///     (<c>.../issues/{n}/comments</c>).
/// </summary>
internal sealed record GitHubCommentBodyDto(
    [property: JsonPropertyName("body")] string Body);

/// <summary>Ответ GitHub на создание коммента: id + html_url созданной записи (#713).</summary>
internal sealed record GitHubCommentResponseDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("html_url")] string HtmlUrl);
