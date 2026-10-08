namespace ProgressService.Core.Abstractions;

/// <summary>
/// Начисляет XP пользователю по завершённым действиям и обеспечивает идемпотентность начисления.
/// </summary>
public interface IXpAwardService
{
    /// <summary>
    /// Пытается выдать награду XP по бизнес-источнику события.
    /// Если награда уже была выдана ранее, метод завершится успешно без повторного начисления.
    /// </summary>
    Task<UnitResult<Error>> AwardAsync(XpAwardCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Отзывает ранее выданную награду XP по бизнес-источнику. Идемпотентен: если награды
    /// нет — успех без действий. Если есть — удаляет ledger-запись, списывает XP и
    /// пересчитывает уровень (уровень может понизиться). Используется при reopen ревью.
    /// </summary>
    Task<UnitResult<Error>> RevokeAsync(XpAwardCommand command, CancellationToken cancellationToken);
}
