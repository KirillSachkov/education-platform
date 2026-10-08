using System.Linq.Expressions;
using AccessService.Domain.TgJoinReminders;

namespace AccessService.Core.Database;

public interface ITgJoinRemindersRepository
{
    Task AddAsync(TgJoinReminder reminder, CancellationToken ct = default);

    Task<TgJoinReminder?> GetByUserAndPlanAsync(Guid userId, Guid planId, CancellationToken ct = default);

    Task<IReadOnlyList<TgJoinReminder>> GetManyByAsync(
        Expression<Func<TgJoinReminder, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<TgJoinReminder, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    ///     Returns up to <paramref name="take"/> active reminder rows
    ///     (<c>CompletedAt IS NULL AND RemindersSent &lt; maxReminders</c>) ordered by
    ///     <c>CreatedAt</c> ASC, so the oldest (most overdue) rows are processed first.
    ///     Used by <c>TgJoinReminderSweeper</c> — bounded LIMIT avoids loading an
    ///     unbounded set on a grant storm.
    /// </summary>
    Task<IReadOnlyList<TgJoinReminder>> GetDueBatchAsync(
        int maxReminders,
        int take,
        CancellationToken ct = default);
}
