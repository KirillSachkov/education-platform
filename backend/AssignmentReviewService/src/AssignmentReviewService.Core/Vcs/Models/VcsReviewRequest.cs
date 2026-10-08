namespace AssignmentReviewService.Core.Vcs.Models;

/// <summary>
///     Запрос на публикацию AI review в PR. <c>CommitSha</c> — PR HEAD на момент
///     запуска iteration; защищает от race-условия (если студент успел запушить
///     новый commit пока AI считал, GitHub отвергнет review с outdated commit_id).
///     Event на нашей стороне всегда <c>COMMENT</c>; не делаем REQUEST_CHANGES.
/// </summary>
public sealed record VcsReviewRequest(
    string CommitSha,
    string SummaryBody,
    IReadOnlyList<VcsReviewComment> InlineComments);
