namespace ProgressService.Core.Abstractions;

/// <summary>
/// Определяет правила вычисления текущего и следующего уровня пользователя по накопленному XP.
/// </summary>
public interface IXpLevelPolicy
{
    /// <summary>
    /// Возвращает текущий уровень пользователя для указанного общего количества XP.
    /// </summary>
    int ResolveLevel(int totalXp);

    /// <summary>
    /// Возвращает следующий уровень или <c>null</c>, если пользователь уже достиг максимального уровня.
    /// </summary>
    int? ResolveNextLevel(int totalXp);

    /// <summary>
    /// Возвращает количество XP, необходимое для достижения следующего уровня,
    /// или <c>null</c>, если следующего уровня нет.
    /// </summary>
    int? ResolveXpToNextLevel(int totalXp);
}
