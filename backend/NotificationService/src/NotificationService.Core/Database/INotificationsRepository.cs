using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using NotificationService.Domain.Notifications;
using SharedKernel;

namespace NotificationService.Core.Database;

public interface INotificationsRepository
{
    Task<Result<Notification, Error>> GetBy(
        Expression<Func<Notification, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListForUserAsync(
        Guid userId,
        int limit,
        DateTime? cursorBefore,
        Guid? cursorId,
        bool unreadOnly,
        IReadOnlyList<short>? types,
        CancellationToken cancellationToken = default);

    Task<int> CountBy(
        Expression<Func<Notification, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsBy(
        Expression<Func<Notification, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Есть ли у получателя <b>непрочитанное</b> уведомление данного типа по конкретной
    ///     сдаче (<c>payload.submissionId</c>). Коалесинг пуш-спама (#713): пока автор не
    ///     прочитал вопрос по сдаче, повторные вопросы по ней не плодят новый пуш.
    /// </summary>
    Task<bool> HasUnreadOfTypeForSubmissionAsync(
        Guid recipientUserId,
        NotificationType type,
        Guid submissionId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

    Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
