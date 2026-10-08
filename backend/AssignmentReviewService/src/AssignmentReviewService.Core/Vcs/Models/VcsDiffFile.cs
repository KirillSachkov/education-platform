namespace AssignmentReviewService.Core.Vcs.Models;

public sealed record VcsDiffFile(
    string Path,
    string? OldPath,
    string Status,
    int Additions,
    int Deletions,
    string? Patch,
    IReadOnlyList<VcsHunk> Hunks);
