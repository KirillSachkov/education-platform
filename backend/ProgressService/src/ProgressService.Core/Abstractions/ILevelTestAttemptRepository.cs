using System.Linq.Expressions;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий попыток level-test'а (<see cref="LevelTestAttempt"/>).
///     Попыток на квиз сколько угодно; анонимные попытки клеймятся пачкой по
///     <c>AnonymousId</c>. Issue #479.
/// </summary>
public interface ILevelTestAttemptRepository
{
    Task AddAsync(LevelTestAttempt attempt, CancellationToken cancellationToken = default);

    Task<LevelTestAttempt?> GetByAsync(
        Expression<Func<LevelTestAttempt, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LevelTestAttempt>> GetManyByAsync(
        Expression<Func<LevelTestAttempt, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>Последняя попытка пользователя по времени создания (#528); нет — null.</summary>
    Task<LevelTestAttempt?> GetLatestByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
