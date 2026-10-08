namespace AssignmentReviewService.Core.Vcs.Models;

public sealed record VcsReviewComment(
    string Path,
    int Line,
    string Body,
    string? Suggestion = null);
