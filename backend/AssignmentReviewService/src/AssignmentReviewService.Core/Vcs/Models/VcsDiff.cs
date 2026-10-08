namespace AssignmentReviewService.Core.Vcs.Models;

public sealed record VcsDiff(
    string HeadSha,
    IReadOnlyList<VcsDiffFile> Files,
    int TotalAdditions,
    int TotalDeletions);
