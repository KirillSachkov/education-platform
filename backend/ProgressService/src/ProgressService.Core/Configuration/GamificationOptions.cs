using System.Collections.ObjectModel;

namespace ProgressService.Core.Configuration;

/// <summary>
/// Корневая секция конфигурации геймификации.
/// </summary>
public sealed class GamificationOptions
{
    /// <summary>
    /// Имя секции в configuration providers.
    /// </summary>
    public const string SECTION_NAME = "Gamification";

    /// <summary>
    /// Размеры наград XP за отдельные типы действий.
    /// </summary>
    public GamificationAwardsOptions Awards { get; init; } = new();

    /// <summary>
    /// Набор уровней и XP-порогов для их достижения.
    /// </summary>
    public Collection<GamificationLevelOptions> Levels { get; init; } = [];
}

/// <summary>
/// Содержит размеры наград XP за конкретные завершённые действия.
/// </summary>
public sealed class GamificationAwardsOptions
{
    /// <summary>XP за одобрение задачи.</summary>
    public int IssueApproved { get; init; }

    /// <summary>XP за завершение модуля.</summary>
    public int ModuleCompleted { get; init; }

    /// <summary>XP за завершение проекта.</summary>
    public int ProjectCompleted { get; init; }

    /// <summary>XP за просмотр материала.</summary>
    public int MaterialViewed { get; init; }
}

/// <summary>
/// Описывает один уровень и минимальный порог XP для его достижения.
/// </summary>
public sealed class GamificationLevelOptions
{
    /// <summary>Порядковый номер уровня.</summary>
    public int Level { get; init; }

    /// <summary>Минимальное количество XP, необходимое для достижения уровня.</summary>
    public int RequiredXp { get; init; }
}
