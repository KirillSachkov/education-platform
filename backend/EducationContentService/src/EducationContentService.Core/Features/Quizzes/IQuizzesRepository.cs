using System.Linq.Expressions;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Core.Features.Quizzes;

/// <summary>
///     Репозиторий агрегата <see cref="Quiz"/>. Вопросы (JSONB) загружаются вместе
///     со строкой — отдельный eager-load не нужен.
/// </summary>
public interface IQuizzesRepository
{
    Task AddAsync(Quiz quiz, CancellationToken cancellationToken = default);

    void Delete(Quiz quiz);

    Task<Result<Quiz, Error>> GetByAsync(
        Expression<Func<Quiz, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Quiz>> GetManyByAsync(
        Expression<Func<Quiz, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<Quiz, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Уникальность заголовка среди non-DRAFT квизов (PUBLISHED + ARCHIVED) — зеркалит
    ///     filtered-индекс <c>ix_quizzes_title</c> и поведение <c>IMaterialsRepository</c>.
    /// </summary>
    Task<bool> ExistsByTitleAsync(Title title, Guid? excludeId, CancellationToken cancellationToken = default);
}
