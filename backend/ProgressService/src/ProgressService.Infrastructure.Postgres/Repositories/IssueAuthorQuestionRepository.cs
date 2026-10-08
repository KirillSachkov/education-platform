using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.AuthorQuestions;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class IssueAuthorQuestionRepository : IIssueAuthorQuestionRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public IssueAuthorQuestionRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task AddAsync(IssueAuthorQuestion question, CancellationToken cancellationToken = default)
    {
        await _dbContext.IssueAuthorQuestions.AddAsync(question, cancellationToken);
    }

    public Task<IssueAuthorQuestion?> GetByAsync(
        Expression<Func<IssueAuthorQuestion, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.IssueAuthorQuestions.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public Task<bool> ExistsAsync(
        Expression<Func<IssueAuthorQuestion, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.IssueAuthorQuestions.AnyAsync(predicate, cancellationToken);
    }

    public async Task<int> DeleteByIssueIdAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM issue_author_questions WHERE issue_id = @IssueId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { IssueId = issueId },
                cancellationToken: cancellationToken));
    }
}
