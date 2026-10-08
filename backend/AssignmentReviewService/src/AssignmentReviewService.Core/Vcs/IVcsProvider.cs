using AssignmentReviewService.Core.Vcs.Models;

namespace AssignmentReviewService.Core.Vcs;

/// <summary>
///     Контракт VCS-провайдера. Phase 3 — единственная реализация
///     <c>GitHubVcsProvider</c>; слой готов к расширению на GitLab / Bitbucket
///     по идентичной форме (см. spec §Architecture).
/// </summary>
public interface IVcsProvider
{
    /// <summary>Тип провайдера для логирования и фильтрации в БД.</summary>
    Domain.Vcs.VcsProvider Provider { get; }

    /// <summary>Получить metadata PR'а (title, head SHA, author, base/head branch).</summary>
    Task<Result<VcsPullRequest, Error>> GetPullRequestAsync(
        string installationId,
        string repoFullName,
        int pullNumber,
        CancellationToken ct = default);

    /// <summary>Получить diff PR'а — структурированный (file → hunks → lines).</summary>
    Task<Result<VcsDiff, Error>> GetPullRequestDiffAsync(
        string installationId,
        string repoFullName,
        int pullNumber,
        CancellationToken ct = default);

    /// <summary>
    ///     Incremental diff между двумя коммитами (#17). Используется для re-review:
    ///     <paramref name="baseSha"/> — head предыдущей completed-итерации,
    ///     <paramref name="headSha"/> — текущий head PR'а. Возвращает тот же
    ///     <see cref="VcsDiff"/>-shape, что и <see cref="GetPullRequestDiffAsync"/>;
    ///     <see cref="VcsDiff.HeadSha"/> = <paramref name="headSha"/>. Пустой
    ///     список файлов = нет новых изменений между коммитами.
    /// </summary>
    Task<Result<VcsDiff, Error>> CompareAsync(
        string installationId,
        string repoFullName,
        string baseSha,
        string headSha,
        CancellationToken ct = default);

    /// <summary>
    ///     Запостить review (summary + inline comments) в PR. На нашей стороне всегда
    ///     <c>event=COMMENT</c> — AI не блокирует ручной workflow автора (см. spec §Scope).
    /// </summary>
    Task<Result<VcsPostedReview, Error>> PostReviewAsync(
        string installationId,
        string repoFullName,
        int pullNumber,
        VcsReviewRequest review,
        CancellationToken ct = default);

    /// <summary>
    ///     Запостить ответ автора курса обратно в тред PR (#713). Способ зависит от
    ///     <paramref name="kind"/> исходного сообщения студента:
    ///     <list type="bullet">
    ///         <item><see cref="Domain.Reviews.StudentPrMessageKind.REVIEW_COMMENT"/> — reply
    ///         на inline review-коммент (GitHub <c>POST /repos/{owner}/{repo}/pulls/{pull}/comments/{id}/replies</c>,
    ///         <paramref name="inReplyToCommentId"/> — id того коммента студента, на который отвечаем).</item>
    ///         <item><see cref="Domain.Reviews.StudentPrMessageKind.ISSUE_COMMENT"/> — новый top-level
    ///         коммент в ленте PR (GitHub <c>POST /repos/{owner}/{repo}/issues/{pull}/comments</c>;
    ///         <paramref name="inReplyToCommentId"/> игнорируется).</item>
    ///     </list>
    ///     Возвращает id + html_url созданного ответа. Аутентификация — installation token
    ///     GitHub App (как в <see cref="PostReviewAsync"/>).
    /// </summary>
    Task<Result<VcsPostedComment, Error>> PostCommentReplyAsync(
        string installationId,
        string repoFullName,
        int pullNumber,
        Domain.Reviews.StudentPrMessageKind kind,
        long inReplyToCommentId,
        string body,
        CancellationToken ct = default);

    /// <summary>
    ///     Получить inline-комментарии конкретного ранее опубликованного review'а
    ///     (#383). Используется на re-review, чтобы передать модели тела комментариев
    ///     прошлой итерации (что именно мы просили исправить) — так модель видит, был ли
    ///     вопрос закрыт, и не поднимает уже исправленное заново. <paramref name="reviewId"/>
    ///     — это <c>GitHubReviewId</c>, сохранённый на прошлой completed-итерации.
    ///     Пустой список = у review'а не было inline-комментариев (только summary).
    /// </summary>
    Task<Result<IReadOnlyList<VcsReviewComment>, Error>> GetReviewCommentsAsync(
        string installationId,
        string repoFullName,
        int pullNumber,
        long reviewId,
        CancellationToken ct = default);

    /// <summary>Получить рекурсивное дерево репо (для индексации ref-repo'а).</summary>
    Task<Result<IReadOnlyList<VcsRepoTreeEntry>, Error>> GetRepoTreeAsync(
        string installationId,
        string repoFullName,
        string branch,
        CancellationToken ct = default);

    /// <summary>Получить содержимое файла (base64). Используется при ref-repo индексации.</summary>
    Task<Result<VcsFileContent, Error>> GetFileContentAsync(
        string installationId,
        string repoFullName,
        string path,
        string? branch = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Получить metadata installation'а после установки App: owner info + whitelist
    ///     репозиториев. Используется в Phase 4 install-callback и при ре-fetch'е
    ///     в webhook handler'е (installation_repositories events).
    /// </summary>
    Task<Result<VcsInstallationDetail, Error>> GetInstallationDetailAsync(
        long installationId, CancellationToken ct = default);
}
