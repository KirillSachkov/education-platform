using System.Linq.Expressions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Bookmarks;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class BookmarkedQuestionsRepository : IBookmarkedQuestionsRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public BookmarkedQuestionsRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(BookmarkedQuestion bookmark, CancellationToken ct = default) =>
        await _dbContext.BookmarkedQuestions.AddAsync(bookmark, ct);

    public Task RemoveAsync(BookmarkedQuestion bookmark, CancellationToken ct = default)
    {
        _dbContext.BookmarkedQuestions.Remove(bookmark);
        return Task.CompletedTask;
    }

    public async Task<Result<BookmarkedQuestion, Error>> GetByAsync(
        Expression<Func<BookmarkedQuestion, bool>> predicate,
        CancellationToken ct = default)
    {
        BookmarkedQuestion? bookmark = await _dbContext.BookmarkedQuestions.FirstOrDefaultAsync(predicate, ct);
        return bookmark is null
            ? TrainerServiceErrors.Bookmark.NotFound(Guid.Empty)
            : bookmark;
    }

    public async Task<IReadOnlyList<BookmarkedQuestion>> GetManyByAsync(
        Expression<Func<BookmarkedQuestion, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.BookmarkedQuestions.Where(predicate).ToListAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<BookmarkedQuestion, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.BookmarkedQuestions.AnyAsync(predicate, ct);

    public async Task<IReadOnlyList<BookmarkedQuestion>> GetPageForUserAsync(
        Guid userId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken ct = default)
    {
        IQueryable<BookmarkedQuestion> query = _dbContext.BookmarkedQuestions
            .Where(b => b.UserId == userId);

        // Keyset на (CreatedAt DESC, Id DESC): следующая страница — строго «меньше» курсора.
        // Tuple-сравнение EF Core не транслирует, поэтому раскрываем вручную.
        if (cursorCreatedAt is { } createdAt && cursorId is { } id)
        {
            query = query.Where(b =>
                b.CreatedAt < createdAt || (b.CreatedAt == createdAt && b.Id < id));
        }

        return await query
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Take(limit)
            .ToListAsync(ct);
    }
}
