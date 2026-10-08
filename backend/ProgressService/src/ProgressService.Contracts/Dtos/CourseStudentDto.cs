namespace ProgressService.Contracts.Dtos;

/// <summary>
/// Состояние enrollment для admin/author UI списка учеников курса.
/// access-derive-model (#367): roster derives from AccessService grants; локальная
/// enrollment-строка — это lazy progress-anchor (без archive-состояния).
/// </summary>
public sealed record CourseStudentDto(
    Guid EnrollmentId,
    Guid UserId,
    string? Name,
    string? Username,
    string? Email,
    Guid? AvatarId,
    DateTime EnrolledAt);
