using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using SharedKernel;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Database;

public interface IChatBindingRepository
{
    Task AcquirePlanMutationLockAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<Result<ChatBinding, Error>> GetByAsync(
        Expression<Func<ChatBinding, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatBinding>> GetManyByAsync(
        Expression<Func<ChatBinding, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<ChatBinding, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task AddAsync(ChatBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Маркирует <paramref name="binding"/> как Modified для следующего SaveChanges.
    ///     Нужно потому что <see cref="GetManyByAsync"/> возвращает untracked entities (perf
    ///     для read-heavy путей); для мутаций (например, health-check) caller должен явно
    ///     зарегистрировать изменения.
    /// </summary>
    void Update(ChatBinding binding);

    Task<int> RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> RemoveByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<int> CountByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatBinding>> GetHealthCheckBatchAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
