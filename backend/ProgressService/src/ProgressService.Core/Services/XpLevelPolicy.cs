using Microsoft.Extensions.Options;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Configuration;

namespace ProgressService.Core.Services;

/// <summary>
/// Определяет текущий и следующий уровень пользователя по накопленному опыту
/// на основе конфигурации порогов из appsettings.
/// </summary>
public sealed class XpLevelPolicy : IXpLevelPolicy
{
    private readonly IReadOnlyList<GamificationLevelOptions> _levels;

    public XpLevelPolicy(IOptions<GamificationOptions> options)
    {
        _levels = options.Value.Levels
            .OrderBy(x => x.Level)
            .ToArray();
    }

    /// <summary>
    /// Возвращает текущий уровень пользователя для указанного количества опыта.
    /// Используется после начисления XP, когда нужно сохранить актуальный level.
    /// </summary>
    public int ResolveLevel(int totalXp)
    {
        int normalizedXp = Math.Max(totalXp, 0);
        int currentLevel = _levels[0].Level;

        foreach (GamificationLevelOptions level in _levels)
        {
            if (normalizedXp < level.RequiredXp)
            {
                break;
            }

            currentLevel = level.Level;
        }

        return currentLevel;
    }

    /// <summary>
    /// Возвращает следующий уровень, которого пользователь ещё не достиг.
    /// Если текущий уровень уже максимальный, возвращает <c>null</c>.
    /// </summary>
    public int? ResolveNextLevel(int totalXp)
    {
        int normalizedXp = Math.Max(totalXp, 0);

        GamificationLevelOptions? nextLevel = _levels.FirstOrDefault(x => x.RequiredXp > normalizedXp);

        return nextLevel?.Level;
    }

    /// <summary>
    /// Возвращает, сколько опыта осталось до следующего уровня.
    /// Если следующего уровня нет, возвращает <c>null</c>.
    /// </summary>
    public int? ResolveXpToNextLevel(int totalXp)
    {
        int normalizedXp = Math.Max(totalXp, 0);

        GamificationLevelOptions? nextLevel = _levels.FirstOrDefault(x => x.RequiredXp > normalizedXp);
        if (nextLevel is null)
        {
            return null;
        }

        return nextLevel.RequiredXp - normalizedXp;
    }
}
