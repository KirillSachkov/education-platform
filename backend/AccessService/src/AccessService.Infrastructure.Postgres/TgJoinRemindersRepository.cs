using System.Linq.Expressions;
using AccessService.Core.Database;
using AccessService.Domain.TgJoinReminders;

namespace AccessService.Infrastructure.Postgres;

public sealed class TgJoinRemindersRepository : ITgJoinRemindersRepository
{
    private readonly AccessServiceDbContext _db;

    public TgJoinRemindersRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(TgJoinReminder reminder, CancellationToken ct = default) =>
        await _db.TgJoinReminders.AddAsync(reminder, ct);

    public async Task<TgJoinReminder?> GetByUserAndPlanAsync(
        Guid userId, Guid planId, CancellationToken ct = default) =>
        await _db.TgJoinReminders.FirstOrDefaultAsync(
            r => r.UserId == userId && r.PlanId == planId, ct);

    public async Task<IReadOnlyList<TgJoinReminder>> GetManyByAsync(
        Expression<Func<TgJoinReminder, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.TgJoinReminders.Where(predicate).ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        Expression<Func<TgJoinReminder, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.TgJoinReminders.AnyAsync(predicate, ct);

    public async Task<IReadOnlyList<TgJoinReminder>> GetDueBatchAsync(
        int maxReminders, int take, CancellationToken ct = default) =>
        await _db.TgJoinReminders
            .Where(r => r.CompletedAt == null && r.RemindersSent < maxReminders)
            .OrderBy(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
}
