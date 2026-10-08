namespace ProgressService.Contracts.Requests;

public sealed class GetCourseStudentsRequest
{
    public Guid CourseId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string? Search { get; init; }
}
