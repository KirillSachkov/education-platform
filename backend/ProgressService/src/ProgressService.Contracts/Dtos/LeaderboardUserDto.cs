namespace ProgressService.Contracts.Dtos;

public sealed record LeaderboardUserDto(
    int Rank,
    Guid UserId,
    string? DisplayName,
    string? Username,
    int TotalXp,
    int CurrentLevel,
    bool IsCurrentUser)
{
    public Guid? AvatarId { get; init; }

    // Issue #572 — глобальные счётчики активности участника (по всей платформе,
    // не scoped по курсу/автору). Заполняются на шаге обогащения батч-запросом
    // по userIds текущей страницы; по умолчанию 0.
    /// <summary>Сколько уроков (материалов) участник отметил изученными.</summary>
    public int MaterialsCompleted { get; init; }

    /// <summary>Сколько тестов (квизов) участник прошёл (distinct passed quiz, level-test исключён).</summary>
    public int QuizzesCompleted { get; init; }

    /// <summary>Сколько задач участник решил (distinct COMPLETED issue_progress).</summary>
    public int IssuesCompleted { get; init; }
}
