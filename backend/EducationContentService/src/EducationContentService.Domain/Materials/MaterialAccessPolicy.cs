namespace EducationContentService.Domain.Materials;

/// <summary>
///     Политика уровня доступа материала.
///     <para>
///         После plan-bound рефакторинга (#77) ENROLLED больше не требует привязки к курсу:
///         orphan-материал гейтится через global <c>plan:all</c>, а author lifetime tag
///         остаётся только legacy-alias до полного resync'а. FREE удалён в #358:
///         бесплатный доступ = REGISTERED.
///     </para>
///     <para>
///         Метод <see cref="CanSetAccessType"/> оставлен в API для будущих расширений
///         (например, capability-gating), но сейчас разрешает любую комбинацию access-type
///         и количества привязок к курсам.
///     </para>
/// </summary>
public static class MaterialAccessPolicy
{
    /// <summary>
    ///     Проверяет, можно ли установить указанный <paramref name="accessType"/>.
    ///     После #77 любая комбинация валидна — orphan FREE/ENROLLED разрешены.
    /// </summary>
    public static UnitResult<Error> CanSetAccessType(AccessType accessType, int boundCourseCount)
    {
        if (boundCourseCount < 0)
            throw new ArgumentOutOfRangeException(nameof(boundCourseCount), "Количество привязок не может быть отрицательным");

        // accessType намеренно не используется — после plan-bound рефакторинга все 4 варианта
        // легитимны при любом количестве привязок. Параметр сохранён в сигнатуре для
        // будущих ограничений (capability-based / per-plan allowlist).
        _ = accessType;

        return UnitResult.Success<Error>();
    }
}
