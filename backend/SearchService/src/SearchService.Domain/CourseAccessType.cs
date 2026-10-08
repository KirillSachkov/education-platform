namespace SearchService.Domain;

/// <summary>
/// Legacy enum, оставлен для совместимости со схемой Typesense (поле
/// <c>course_access_type</c> в <c>TypesenseSchemas.cs</c>). Все документы теперь
/// пишутся с <c>CourseAccessType = null</c> (issue #358 — collapse FREE→system default).
/// Удаление потребовало бы full reindex + schema migration.
/// </summary>
public enum CourseAccessType
{
    /// <summary>
    /// Deprecated. Не используется в новых документах. Историческое значение:
    /// «бесплатный доступ» (легаси-флаг до plan-cutover'а).
    /// </summary>
    FREE,
}
