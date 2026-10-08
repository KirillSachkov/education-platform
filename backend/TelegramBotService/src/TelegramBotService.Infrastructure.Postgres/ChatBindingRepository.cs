using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Infrastructure.Postgres;

public sealed class ChatBindingRepository : IChatBindingRepository
{
    private readonly TelegramBotDbContext _dbContext;

    public ChatBindingRepository(TelegramBotDbContext dbContext) => _dbContext = dbContext;

    public async Task AcquirePlanMutationLockAsync(
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({planId.ToString()}::text, 0));",
            cancellationToken);
    }

    public async Task<Result<ChatBinding, Error>> GetByAsync(
        Expression<Func<ChatBinding, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ChatBinding? binding = await _dbContext.ChatBindings
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (binding is null)
            return TelegramBotErrors.ChatBindingNotFound();

        return binding;
    }

    public async Task<IReadOnlyList<ChatBinding>> GetManyByAsync(
        Expression<Func<ChatBinding, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ChatBindings
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(
        Expression<Func<ChatBinding, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        _dbContext.ChatBindings.AnyAsync(predicate, cancellationToken);

    public async Task AddAsync(ChatBinding binding, CancellationToken cancellationToken = default)
    {
        await _dbContext.ChatBindings.AddAsync(binding, cancellationToken);
    }

    public void Update(ChatBinding binding) => _dbContext.ChatBindings.Update(binding);

    public Task<int> RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.ChatBindings
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> RemoveByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
        _dbContext.ChatBindings
            .Where(x => x.PlanId == planId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> CountByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
        _dbContext.ChatBindings.CountAsync(x => x.PlanId == planId, cancellationToken);

    public async Task<IReadOnlyList<ChatBinding>> GetHealthCheckBatchAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ChatBindings
            .AsNoTracking()
            .OrderBy(x => x.LastValidatedAt)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
