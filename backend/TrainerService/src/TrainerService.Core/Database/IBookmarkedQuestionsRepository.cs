using System.Linq.Expressions;
using TrainerService.Domain.Bookmarks;

namespace TrainerService.Core.Database;

public interface IBookmarkedQuestionsRepository
{
    Task AddAsync(BookmarkedQuestion bookmark, CancellationToken ct = default);

    Task RemoveAsync(BookmarkedQuestion bookmark, CancellationToken ct = default);

    Task<Result<BookmarkedQuestion, Error>> GetByAsync(
        Expression<Func<BookmarkedQuestion, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<BookmarkedQuestion>> GetManyByAsync(
        Expression<Func<BookmarkedQuestion, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<BookmarkedQuestion, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    ///     Keyset-страница закладок пользователя, упорядоченная <c>CreatedAt DESC, Id DESC</c>.
    ///     Курсор (<paramref name="cursorCreatedAt"/>, <paramref name="cursorId"/>) — последний
    ///     элемент предыдущей страницы; null = первая страница. Тянет <paramref name="limit"/> + 1
    ///     строк, чтобы caller детектил «есть ещё».
    /// </summary>
    Task<IReadOnlyList<BookmarkedQuestion>> GetPageForUserAsync(
        Guid userId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken ct = default);
}
