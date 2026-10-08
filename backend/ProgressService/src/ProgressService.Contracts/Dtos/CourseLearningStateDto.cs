namespace ProgressService.Contracts.Dtos;

/// <summary>
///     Полное состояние ученика по курсу для course-home/course-overview.
///     Phase E (#45): EnrollmentStatus/EnrollmentType поля удалены —
///     TRIAL/STANDARD различия и SUSPENDED/ACTIVE состояния больше нет.
///     <para>
///         <see cref="PassedQuizIds"/> (ST-16 #495) — distinct id квизов из blueprint курса,
///         по которым у юзера есть passed-попытка (user-scoped, как материалы). Питает
///         галочку «пройден» на quiz-строках программы курса.
///     </para>
/// </summary>
public sealed record CourseLearningStateDto(
    Guid EnrollmentId,
    Guid CourseId,
    CourseLearningSummaryDto Summary,
    IReadOnlyList<IssueLearningItemDto> Issues,
    IReadOnlyList<MaterialLearningItemDto> Materials,
    DateTime EnrolledAt,
    CoursePositionDto? LastPosition,
    IReadOnlyList<Guid> PassedQuizIds);
