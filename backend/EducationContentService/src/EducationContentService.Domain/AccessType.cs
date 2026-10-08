namespace EducationContentService.Domain;

/// <summary>
///     Тип доступа к образовательному контенту — определяет,
///     какие пользователи могут видеть/открывать ресурс.
/// </summary>
/// <remarks>
///     Порядок значений — от наиболее разрешительного к наиболее ограниченному.
///     Issue #358: legacy <c>FREE</c> value removed. Бесплатный доступ теперь =
///     system default (любой залогиненный юзер) через <see cref="REGISTERED"/>.
/// </remarks>
public enum AccessType
{
    /// <summary>Публичный — доступен любому, без авторизации.</summary>
    PUBLIC,

    /// <summary>Для зарегистрированных — любой залогиненный пользователь без зачисления.</summary>
    REGISTERED,

    /// <summary>Только для зачисленных на курс (STANDARD enrollment).</summary>
    ENROLLED
}
