namespace AssignmentReviewService.Core.Vcs.Models;

/// <summary>
///     Результат публикации одиночного коммента/reply автора в тред PR (#713):
///     созданный GitHub comment id + deep-link на него.
/// </summary>
public sealed record VcsPostedComment(long GitHubCommentId, string HtmlUrl);
