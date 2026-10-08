namespace AssignmentReviewService.Core.Vcs.Models;

public sealed record VcsRepoTreeEntry(
    string Path,
    string Type,
    long? Size,
    string Sha);
