namespace ProgressService.Contracts.Responses;

public sealed record CoursePublicStatsResponse(
    long EnrolledStudentsCount,
    long CompletedStudentsCount,
    double AverageProgressPercent);
