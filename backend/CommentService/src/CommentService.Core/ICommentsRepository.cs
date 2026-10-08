using System.Linq.Expressions;
using CommentService.Domain;
using Common;

namespace CommentService.Core;

public interface ICommentsRepository
{
    Task<Result<Comment, Error>> GetBy(Expression<Func<Comment, bool>> predicate, CancellationToken cancellationToken = default);

    Task AddAsync(Comment comment, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Hard-deletes ВСЕ комментарии (включая soft-deleted) для конкретной target-сущности.
    ///     Используется handler'ами hard-delete событий из других сервисов: когда исходный материал /
    ///     задача / квиз удалены навсегда, треды комментариев становятся unreachable orphan-данными
    ///     и должны полностью исчезнуть.
    /// </summary>
    Task<int> DeleteByTargetEntityAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);

}