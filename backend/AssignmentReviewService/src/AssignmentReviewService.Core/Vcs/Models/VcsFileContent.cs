namespace AssignmentReviewService.Core.Vcs.Models;

public sealed record VcsFileContent(
    string Path,
    string Sha,
    long Size,
    string Encoding,
    string Content);
