namespace EducationContentService.Domain.Collections;

/// <summary>
///     Политика уровня доступа подборки.
///     <para>
    ///         После plan-bound рефакторинга (#77) ENROLLED больше не требует привязки к курсу:
    ///         platform-level подборка гейтится через платформенный <c>plan:all</c>.
///     </para>
/// </summary>
public static class CollectionAccessPolicy
{
    /// <summary>
    ///     Проверяет, можно ли установить указанный <paramref name="accessType"/> у подборки
    ///     с заданным <paramref name="courseId"/>. После #77 любая комбинация валидна.
    /// </summary>
    public static UnitResult<Error> CanSetAccessType(AccessType accessType, Guid? courseId)
    {
        // Параметры сохранены в сигнатуре для будущих ограничений; сейчас все комбинации легитимны.
        _ = accessType;
        _ = courseId;

        return UnitResult.Success<Error>();
    }
}
