using System.Linq.Expressions;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий попыток прохождения квизов (<see cref="QuizAttempt"/>).
///     Попыток на пару (UserId, QuizId) может быть несколько — best/last
///     вычисляются на чтении. Issue #470.
/// </summary>
public interface IQuizAttemptRepository
{
    Task AddAsync(QuizAttempt attempt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuizAttempt>> GetManyByAsync(
        Expression<Func<QuizAttempt, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<(QuizAttempt? Best, QuizAttempt? Latest)> GetBestAndLatestAsync(
        Guid userId,
        Guid quizId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Каскад на <c>quiz.hard_deleted</c> (ST-13 #493): сносит все попытки удалённого квиза.
    /// </summary>
    Task<int> DeleteByQuizIdAsync(Guid quizId, CancellationToken cancellationToken = default);
}
