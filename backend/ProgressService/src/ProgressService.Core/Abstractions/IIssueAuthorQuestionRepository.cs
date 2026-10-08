using System.Linq.Expressions;
using ProgressService.Domain.AuthorQuestions;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий приватных вопросов студента автору по заданию (<see cref="IssueAuthorQuestion"/>).
///     Ключ: (UserId, IssueId) — один вопрос на пару (идемпотентность). Issue #693.
/// </summary>
public interface IIssueAuthorQuestionRepository
{
    Task AddAsync(IssueAuthorQuestion question, CancellationToken cancellationToken = default);

    Task<IssueAuthorQuestion?> GetByAsync(
        Expression<Func<IssueAuthorQuestion, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<IssueAuthorQuestion, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Каскадное удаление всех вопросов по заданию — вызывается при hard-delete задачи.
    /// </summary>
    Task<int> DeleteByIssueIdAsync(Guid issueId, CancellationToken cancellationToken = default);
}
