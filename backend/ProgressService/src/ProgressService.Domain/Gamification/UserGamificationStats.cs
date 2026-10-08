namespace ProgressService.Domain.Gamification;

/// <summary>
/// Хранит агрегированную статистику пользователя по XP и текущему уровню.
/// </summary>
public sealed class UserGamificationStats
{
    private UserGamificationStats(Guid userId, int currentLevel)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TotalXp = 0;
        CurrentLevel = currentLevel;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private UserGamificationStats()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; private set; }

    public Guid UserId { get; private set; }

    public int TotalXp { get; private set; }

    public int CurrentLevel { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Создаёт начальную статистику пользователя для геймификации.
    /// </summary>
    public static Result<UserGamificationStats, Error> Create(Guid userId, int initialLevel)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (initialLevel <= 0)
        {
            return ProgressErrors.LevelMustBePositive(nameof(initialLevel));
        }

        return new UserGamificationStats(userId, initialLevel);
    }

    /// <summary>
    /// Увеличивает общее количество XP и обновляет текущий уровень пользователя.
    /// </summary>
    public UnitResult<Error> AddXp(int amount, int newLevel)
    {
        if (amount <= 0)
        {
            return ProgressErrors.XpAmountMustBePositive(nameof(amount));
        }

        if (newLevel <= 0)
        {
            return ProgressErrors.LevelMustBePositive(nameof(newLevel));
        }

        if (newLevel < CurrentLevel)
        {
            return ProgressErrors.LevelCannotDecrease();
        }

        TotalXp += amount;
        CurrentLevel = newLevel;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Списывает указанное количество XP с пользователя и пересчитывает уровень (уровень
    /// может понизиться, в отличие от <see cref="AddXp"/>). Используется при reopen ревью,
    /// когда награду нужно отозвать.
    /// </summary>
    public UnitResult<Error> RevokeXp(int amount, int newLevel)
    {
        if (amount <= 0)
        {
            return ProgressErrors.XpAmountMustBePositive(nameof(amount));
        }

        if (newLevel <= 0)
        {
            return ProgressErrors.LevelMustBePositive(nameof(newLevel));
        }

        if (amount > TotalXp)
        {
            return ProgressErrors.XpAmountExceedsTotal();
        }

        TotalXp -= amount;
        CurrentLevel = newLevel;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }
}
