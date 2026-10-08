using System.Linq.Expressions;
using ProgressService.Domain.Gamification;

namespace ProgressService.Core.Abstractions;

public interface IXpAwardRepository
{
    Task AddAsync(XpAward xpAward, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Проверяет существование XP-награды по предикату. Идемпотентность: сверяется
    ///     сначала с tracked-сущностями в текущей транзакции, потом с БД — чтобы не
    ///     начислить дубликат, если предыдущий Add в той же транзакции ещё не SaveChanges.
    /// </summary>
    Task<bool> ExistsAsync(
        Expression<Func<XpAward, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает award по предикату или возвращает null. Нужен для reopen ревью —
    /// чтобы списать ровно начисленный <see cref="XpAward.XpAmount"/>.
    /// </summary>
    Task<XpAward?> FindByAsync(
        Expression<Func<XpAward, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет ledger-запись награды. Используется при reopen ревью.
    /// </summary>
    void Remove(XpAward xpAward);
}
