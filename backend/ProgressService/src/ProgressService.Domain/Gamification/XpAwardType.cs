namespace ProgressService.Domain.Gamification;

/// <summary>
/// Типы действий, за которые пользователь может получить XP.
/// </summary>
public enum XpAwardType
{
    /// <summary>Награда за одобренную задачу.</summary>
    ISSUE_APPROVED,

    /// <summary>Награда за завершение модуля.</summary>
    MODULE_COMPLETED,

    /// <summary>Награда за завершение проекта.</summary>
    PROJECT_COMPLETED,

    /// <summary>Награда за просмотр материала.</summary>
    MATERIAL_VIEWED,
}
