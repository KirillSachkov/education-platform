using System.Linq.Expressions;
using CommentService.Core;
using CommentService.Domain;
using Common;
using Microsoft.Extensions.Logging;

namespace CommentService.Infrastructure.Postgres;

public sealed class CommentsRepository : ICommentsRepository
{
    private readonly CommentDbContext _dbContext;
    private readonly ILogger<CommentsRepository> _logger;

    public CommentsRepository(CommentDbContext dbContext, ILogger<CommentsRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<Comment, Error>> GetBy(
        Expression<Func<Comment, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Comment? comment = await _dbContext.Comments.FirstOrDefaultAsync(predicate, cancellationToken);

        if (comment is null)
        {
            // Not-found is an expected outcome for user input (wrong id / already deleted),
            // not a system warning — keep noise out of Loki and rely on TraceId for correlation.
            _logger.LogDebug("Comment lookup returned no match for predicate {Predicate}", predicate);
            return Error.NotFound("comment.not.found", "Комментарий не найден");
        }

        return comment;
    }

    public async Task AddAsync(Comment comment, CancellationToken cancellationToken = default)
    {
        await _dbContext.Comments.AddAsync(comment, cancellationToken);
    }

    public Task<int> DeleteByTargetEntityAsync(
        EntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Comments
            // IgnoreQueryFilters: захватываем и soft-deleted записи — иначе они оставались бы
            // в таблице (orphan'ы без adressable target'а).
            .IgnoreQueryFilters()
            .Where(c => c.EntityReference.Type == entityType && c.EntityReference.Id == entityId)
            .ExecuteDeleteAsync(cancellationToken);

}