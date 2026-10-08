namespace ProgressService.Contracts.Responses;

/// <summary>
/// Сводка активности пользователя для страницы «Мой прогресс»: дневная сетка
/// за последние 12 недель, стрик и суммарные итоги за всё время.
/// </summary>
public sealed record MyActivityResponse(
    /// <summary>Ровно 84 дня (12 недель), включая сегодня, от старых к новым. Дни без активности — нулевые.</summary>
    IReadOnlyList<ActivityDayDto> Days,
    /// <summary>Серии последовательных дней с активностью.</summary>
    ActivityStreakDto Streak,
    /// <summary>Суммарные итоги за всё время.</summary>
    ActivityTotalsDto Totals);

/// <summary>
/// Активность за один календарный день (UTC).
/// </summary>
public sealed record ActivityDayDto(
    /// <summary>Дата дня (UTC), сериализуется как «yyyy-MM-dd».</summary>
    DateOnly Date,
    /// <summary>Сумма начисленного XP за день.</summary>
    int Xp,
    /// <summary>Количество материалов, отмеченных изученными в этот день.</summary>
    int MaterialsCompleted);

/// <summary>
/// Стрик — серии последовательных дней с любой активностью.
/// </summary>
public sealed record ActivityStreakDto(
    /// <summary>Текущая серия: дни подряд, заканчивающиеся сегодня или вчера.</summary>
    int Current,
    /// <summary>Лучшая серия за всю историю.</summary>
    int Longest);

/// <summary>
/// Суммарные итоги пользователя за всё время.
/// </summary>
public sealed record ActivityTotalsDto(
    /// <summary>Всего накопленного XP.</summary>
    int TotalXp,
    /// <summary>Всего материалов, отмеченных изученными.</summary>
    int MaterialsCompleted,
    /// <summary>Всего принятых задач (XP-награды за одобренные задачи).</summary>
    int IssuesApproved);
