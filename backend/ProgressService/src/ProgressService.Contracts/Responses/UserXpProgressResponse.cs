namespace ProgressService.Contracts.Responses;

/// <summary>
/// Сводка по текущему состоянию XP и уровня пользователя.
/// </summary>
public sealed record UserXpProgressResponse(
    /// <summary>Общее количество накопленного XP.</summary>
    int TotalXp,
    /// <summary>Текущий уровень пользователя.</summary>
    int CurrentLevel,
    /// <summary>Следующий уровень или <c>null</c>, если максимальный уровень уже достигнут.</summary>
    int? NextLevel,
    /// <summary>Количество XP до следующего уровня или <c>null</c>, если следующего уровня нет.</summary>
    int? XpToNextLevel);
