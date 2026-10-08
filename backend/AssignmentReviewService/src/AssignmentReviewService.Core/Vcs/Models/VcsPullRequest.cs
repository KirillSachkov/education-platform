namespace AssignmentReviewService.Core.Vcs.Models;

public sealed record VcsPullRequest(
    string RepoFullName,
    int Number,
    string Title,
    string AuthorLogin,
    string HeadSha,
    string HeadRef,
    string BaseRef,
    string HtmlUrl,
    string State,
    bool IsDraft);
