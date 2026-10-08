namespace ProgressService.Contracts.V2.Responses;

/// <summary>
///     V2: Информация о записи на курс.
/// </summary>
public sealed record CourseEnrollmentResponse(
    Guid EnrollmentId,
    Guid CourseId,
    string Status,
    Guid? LastAccessedItemId,
    int RequiredItemsTotal,
    int RequiredItemsCompleted,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTime UpdatedAt);
