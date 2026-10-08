using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using SharedKernel;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Database;

public interface IUserLinkRepository
{
    Task<Result<UserLink, Error>> GetBy(
        Expression<Func<UserLink, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsBy(
        Expression<Func<UserLink, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task AddAsync(UserLink link, CancellationToken cancellationToken = default);

    Task<int> RemoveByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Обновляет TelegramUsername для существующего link'а если он отличается от переданного.
    ///     Возвращает true если был UPDATE (значение изменилось), false если ничего менять не пришлось
    ///     (или link не существует).
    /// </summary>
    Task<bool> UpdateTelegramUsernameIfChangedAsync(
        long telegramUserId,
        string? telegramUsername,
        CancellationToken cancellationToken = default);
}
