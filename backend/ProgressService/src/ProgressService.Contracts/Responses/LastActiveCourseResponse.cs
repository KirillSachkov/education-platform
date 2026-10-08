using ProgressService.Contracts.Dtos;

namespace ProgressService.Contracts.Responses;

/// <summary>
/// Курс, где пользователь последний раз что-то открывал (материал или задание).
/// <see cref="LastPosition"/> — null если пользователь записался, но ещё ничего не открывал.
/// Phase E (#45): EnrollmentType поле удалено — TRIAL/STANDARD различия больше нет.
/// <see cref="Kind"/> — course kind (COURSE | INTENSIVE | MARATHON) из ECS blueprint, для frontend continue-learning.
/// </summary>
public sealed record LastActiveCourseResponse(
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
    int ProgressPercent,
    bool IsNew,
    DateTime EnrolledAt,
    DateTime? LastActivityAt,
    CoursePositionDto? LastPosition,
    string Kind);
