namespace ProgressService.Contracts.Dtos;

/// <summary>
/// Последняя точка пользователя в курсе — материал или задание, которое он
/// открывал последним. Используется фронтом для CTA «Продолжить курс» и
/// маркера «Продолжить» в программе курса.
/// </summary>
/// <param name="EntityType">"MATERIAL" или "ISSUE".</param>
public sealed record CoursePositionDto(
    string EntityType,
    Guid EntityId,
    DateTime OpenedAt);
