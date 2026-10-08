namespace AssignmentReviewService.Domain.Reviews;

public enum AiReviewStatus
{
    QUEUED = 1,
    RUNNING = 2,
    READY = 3,
    FAILED = 4,
}
