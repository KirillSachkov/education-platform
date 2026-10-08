using System.Linq.Expressions;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Infrastructure.Postgres;

public sealed class UserLinkRepository : IUserLinkRepository
{
    private readonly TelegramBotDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public UserLinkRepository(TelegramBotDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task<Result<UserLink, Error>> GetBy(
        Expression<Func<UserLink, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        UserLink? link = await _dbContext.UserLinks
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (link is null)
            return Error.NotFound("telegram.user.link.not.found", "Связь Telegram-аккаунта не найдена");

        return link;
    }

    public Task<bool> ExistsBy(
        Expression<Func<UserLink, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        _dbContext.UserLinks.AnyAsync(predicate, cancellationToken);

    public async Task AddAsync(UserLink link, CancellationToken cancellationToken = default)
    {
        await _dbContext.UserLinks.AddAsync(link, cancellationToken);
    }

    public Task<int> RemoveByTelegramUserIdAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default) =>
        _dbContext.UserLinks
            .Where(x => x.TelegramUserId == telegramUserId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<bool> UpdateTelegramUsernameIfChangedAsync(
        long telegramUserId,
        string? telegramUsername,
        CancellationToken cancellationToken = default)
    {
        UserLink? link = await _dbContext.UserLinks
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken);

        if (link is null)
            return false;

        string? normalized = string.IsNullOrWhiteSpace(telegramUsername) ? null : telegramUsername;
        if (string.Equals(link.TelegramUsername, normalized, StringComparison.Ordinal))
            return false;

        link.UpdateTelegramUsername(normalized);

        // Routed through ITransactionManager (not _dbContext.SaveChangesAsync) so any future
        // outbox publishes are flushed atomically — see docs/agents/backend-transactions.md.
        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        return saveResult.IsSuccess;
    }
}
