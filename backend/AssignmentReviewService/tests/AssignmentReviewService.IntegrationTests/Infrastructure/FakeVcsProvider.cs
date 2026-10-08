using System.Collections.ObjectModel;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AssignmentReviewService.IntegrationTests.Infrastructure;

/// <summary>
///     In-memory fake IVcsProvider. Phase 4 tests конфигурируют только
///     <see cref="InstallationDetailHandler"/>; Phase 7 tests — PR/diff/post handler'ы.
/// </summary>
public sealed class FakeVcsProvider : IVcsProvider
{
    public VcsProvider Provider => VcsProvider.GITHUB;

    public Func<long, CancellationToken, Task<Result<VcsInstallationDetail, Error>>>?
        InstallationDetailHandler { get; set; }

    public Func<string, string, int, CancellationToken, Task<Result<VcsPullRequest, Error>>>?
        PullRequestHandler { get; set; }

    public Func<string, string, int, CancellationToken, Task<Result<VcsDiff, Error>>>?
        DiffHandler { get; set; }

    public Func<string, string, string, string, CancellationToken, Task<Result<VcsDiff, Error>>>?
        CompareHandler { get; set; }

    public Func<string, string, int, VcsReviewRequest, CancellationToken, Task<Result<VcsPostedReview, Error>>>?
        PostReviewHandler { get; set; }

    public Func<string, string, int, long, CancellationToken, Task<Result<IReadOnlyList<VcsReviewComment>, Error>>>?
        ReviewCommentsHandler { get; set; }

    public Func<string, string, int, StudentPrMessageKind, long, string, CancellationToken, Task<Result<VcsPostedComment, Error>>>?
        PostCommentReplyHandler { get; set; }

    public Collection<(string InstallationId, string RepoFullName, int PullNumber, VcsReviewRequest Request)>
        PostedReviews { get; } = [];

    public Collection<(string InstallationId, string RepoFullName, int PullNumber, StudentPrMessageKind Kind, long InReplyToCommentId, string Body)>
        PostedCommentReplies { get; } = [];

    public Task<Result<VcsInstallationDetail, Error>> GetInstallationDetailAsync(
        long installationId, CancellationToken ct = default)
    {
        if (InstallationDetailHandler is not null)
            return InstallationDetailHandler(installationId, ct);

        VcsInstallationDetail detail = new(
            "test-user",
            "9999",
            VcsInstallationOwnerType.USER,
            RepoSelections.AllRepos());
        return Task.FromResult(Result.Success<VcsInstallationDetail, Error>(detail));
    }

    public Task<Result<VcsPullRequest, Error>> GetPullRequestAsync(
        string installationId, string repoFullName, int pullNumber, CancellationToken ct = default)
    {
        if (PullRequestHandler is not null)
            return PullRequestHandler(installationId, repoFullName, pullNumber, ct);

        return Task.FromResult(Result.Success<VcsPullRequest, Error>(
            new VcsPullRequest(
                repoFullName,
                pullNumber,
                Title: $"Test PR #{pullNumber}",
                AuthorLogin: "test-student",
                HeadSha: "deadbeef",
                HeadRef: "feature/test",
                BaseRef: "main",
                HtmlUrl: $"https://github.com/{repoFullName}/pull/{pullNumber}",
                State: "open",
                IsDraft: false)));
    }

    public Task<Result<VcsDiff, Error>> GetPullRequestDiffAsync(
        string installationId, string repoFullName, int pullNumber, CancellationToken ct = default)
    {
        if (DiffHandler is not null)
            return DiffHandler(installationId, repoFullName, pullNumber, ct);

        VcsDiffFile file = new(
            Path: "src/Foo.cs",
            OldPath: null,
            Status: "modified",
            Additions: 5,
            Deletions: 1,
            Patch: "@@ -1,5 +1,9 @@\n line\n+added\n+more\n",
            Hunks: []);
        VcsDiff diff = new(HeadSha: "deadbeef", Files: [file], TotalAdditions: 5, TotalDeletions: 1);
        return Task.FromResult(Result.Success<VcsDiff, Error>(diff));
    }

    public Task<Result<VcsDiff, Error>> CompareAsync(
        string installationId, string repoFullName, string baseSha, string headSha,
        CancellationToken ct = default)
    {
        if (CompareHandler is not null)
            return CompareHandler(installationId, repoFullName, baseSha, headSha, ct);

        VcsDiffFile file = new(
            Path: "src/Foo.cs",
            OldPath: null,
            Status: "modified",
            Additions: 2,
            Deletions: 0,
            Patch: "@@ -1,5 +1,7 @@\n line\n+incremental\n",
            Hunks: []);
        VcsDiff diff = new(HeadSha: headSha, Files: [file], TotalAdditions: 2, TotalDeletions: 0);
        return Task.FromResult(Result.Success<VcsDiff, Error>(diff));
    }

    public Task<Result<VcsPostedReview, Error>> PostReviewAsync(
        string installationId, string repoFullName, int pullNumber,
        VcsReviewRequest review, CancellationToken ct = default)
    {
        PostedReviews.Add((installationId, repoFullName, pullNumber, review));

        if (PostReviewHandler is not null)
            return PostReviewHandler(installationId, repoFullName, pullNumber, review, ct);

        return Task.FromResult(Result.Success<VcsPostedReview, Error>(
            new VcsPostedReview(GitHubReviewId: 42, HtmlUrl: $"https://github.com/{repoFullName}/pull/{pullNumber}#review-42")));
    }

    public Task<Result<IReadOnlyList<VcsReviewComment>, Error>> GetReviewCommentsAsync(
        string installationId, string repoFullName, int pullNumber, long reviewId,
        CancellationToken ct = default)
    {
        if (ReviewCommentsHandler is not null)
            return ReviewCommentsHandler(installationId, repoFullName, pullNumber, reviewId, ct);

        // Default: prior review had no inline comments (only summary).
        return Task.FromResult(
            Result.Success<IReadOnlyList<VcsReviewComment>, Error>([]));
    }

    public Task<Result<VcsPostedComment, Error>> PostCommentReplyAsync(
        string installationId, string repoFullName, int pullNumber,
        StudentPrMessageKind kind, long inReplyToCommentId, string body, CancellationToken ct = default)
    {
        PostedCommentReplies.Add((installationId, repoFullName, pullNumber, kind, inReplyToCommentId, body));

        if (PostCommentReplyHandler is not null)
            return PostCommentReplyHandler(installationId, repoFullName, pullNumber, kind, inReplyToCommentId, body, ct);

        return Task.FromResult(Result.Success<VcsPostedComment, Error>(
            new VcsPostedComment(GitHubCommentId: 555, HtmlUrl: $"https://github.com/{repoFullName}/pull/{pullNumber}#reply-555")));
    }

    public Func<string, string, string, CancellationToken, Task<Result<IReadOnlyList<VcsRepoTreeEntry>, Error>>>?
        RepoTreeHandler { get; set; }

    public Func<string, string, string, string?, CancellationToken, Task<Result<VcsFileContent, Error>>>?
        FileContentHandler { get; set; }

    /// <summary>
    ///     #798 — in-memory «репозиторий» для repo-context тестов: путь → содержимое.
    ///     Кормит и дерево (blob-записи с размером), и contents (base64).
    /// </summary>
    public Dictionary<string, string> RepoFiles { get; } = new(StringComparer.Ordinal);

    public Task<Result<IReadOnlyList<VcsRepoTreeEntry>, Error>> GetRepoTreeAsync(
        string installationId, string repoFullName, string branch, CancellationToken ct = default)
    {
        if (RepoTreeHandler is not null)
            return RepoTreeHandler(installationId, repoFullName, branch, ct);

        IReadOnlyList<VcsRepoTreeEntry> entries = RepoFiles
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new VcsRepoTreeEntry(
                kv.Key, "blob", System.Text.Encoding.UTF8.GetByteCount(kv.Value), $"sha-{kv.Key}"))
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<VcsRepoTreeEntry>, Error>(entries));
    }

    public Task<Result<VcsFileContent, Error>> GetFileContentAsync(
        string installationId, string repoFullName, string path,
        string? branch = null, CancellationToken ct = default)
    {
        if (FileContentHandler is not null)
            return FileContentHandler(installationId, repoFullName, path, branch, ct);

        if (!RepoFiles.TryGetValue(path, out string? content))
        {
            return Task.FromResult(Result.Failure<VcsFileContent, Error>(
                Error.NotFound("vcs.resource.not_found", $"VCS resource не найден: {repoFullName}:{path}")));
        }

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return Task.FromResult(Result.Success<VcsFileContent, Error>(
            new VcsFileContent(path, $"sha-{path}", bytes.Length, "base64", Convert.ToBase64String(bytes))));
    }

    public void Reset()
    {
        InstallationDetailHandler = null;
        PullRequestHandler = null;
        DiffHandler = null;
        CompareHandler = null;
        PostReviewHandler = null;
        ReviewCommentsHandler = null;
        PostCommentReplyHandler = null;
        RepoTreeHandler = null;
        FileContentHandler = null;
        RepoFiles.Clear();
        PostedReviews.Clear();
        PostedCommentReplies.Clear();
    }
}
