namespace AssignmentReviewService.Contracts.Reviews;

public sealed record SubmitIterationFeedbackRequest(
    bool IsHelpful,
    string? Comment);
