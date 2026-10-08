namespace TrainerService.Domain;

/// <summary>
/// Направление темы внутри трека / Topic direction facet — фильтр в рамках трека.
/// Nullable: DevOps-темы role-agnostic (GENERAL или null). String без CHECK —
/// новый член enum = миграция не нужна.
/// </summary>
public enum TopicDirection
{
    BACKEND,
    FRONTEND,
    FULLSTACK,
    GENERAL,
}
