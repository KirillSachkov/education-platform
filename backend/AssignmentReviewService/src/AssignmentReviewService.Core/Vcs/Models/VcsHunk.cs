namespace AssignmentReviewService.Core.Vcs.Models;

public sealed record VcsHunk(
    int OldStart,
    int OldLines,
    int NewStart,
    int NewLines,
    string Body);
