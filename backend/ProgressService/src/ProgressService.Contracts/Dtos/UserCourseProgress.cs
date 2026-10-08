namespace ProgressService.Contracts.Dtos;

/// <summary>
/// Phase E (#45): EnrollmentType поле удалено — TRIAL/STANDARD различия больше нет.
/// </summary>
public sealed record UserCourseProgress(
    Guid EnrollmentId,
    Guid CourseId,
    string CourseSlug,
    string Title,
    string Description,
    Guid? ImageId,
    string? ImageUrl,
    int TotalItems,
    int CompletedItems,
    int TotalMaterials,
    int CompletedMaterials,
    int TotalIssues,
    int CompletedIssues,
    int TotalModules,
    int CompletedModules,
    int TotalQuizzes,
    int CompletedQuizzes,
    int ProgressPercent,
    bool IsNew,
    string SortKey,
    DateTime EnrolledAt,
    DateTime? LastActivityAt,
    string Kind);
