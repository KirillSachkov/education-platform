namespace EducationContentService.Contracts.Ownership;

/// <summary>
///     Владение сущностью для нотификаций «прокомментировали твой контент».
/// </summary>
/// <param name="CourseId">Курс, к которому относится сущность (для роутинга нотификации).</param>
/// <param name="AuthorId">Автор курса (course-bound) или автор orphan-материала.</param>
/// <param name="CreatedByUserId">
///     Фактический создатель сущности — может отличаться от <see cref="AuthorId"/>,
///     если контент добавил помощник в курс другого автора. Заполняется для author-owned
///     content entities; для самого course остаётся <c>null</c>.
/// </param>
/// <param name="ManagerUserIds">
///     Все пользователи, которые могут редактировать сущность: непосредственный создатель
///     и владельцы всех связанных курсов. Нужен для shared content, где одного
///     <see cref="AuthorId"/> недостаточно.
/// </param>
public sealed record EntityOwnershipDto(
    Guid? CourseId,
    Guid? AuthorId,
    Guid? CreatedByUserId = null,
    IReadOnlyList<Guid>? ManagerUserIds = null);
