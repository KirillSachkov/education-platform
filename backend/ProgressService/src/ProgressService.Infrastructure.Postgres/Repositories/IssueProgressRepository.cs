using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Issues;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class IssueProgressRepository : IIssueProgressRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public IssueProgressRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task AddAsync(IssueProgress issueProgress, CancellationToken cancellationToken = default)
    {
        await _dbContext.IssueProgresses.AddAsync(issueProgress, cancellationToken);
    }

    public async Task<Result<IssueProgress, Error>> GetByAsync(
        Expression<Func<IssueProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        IssueProgress? issueProgress = await _dbContext.IssueProgresses
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return issueProgress is null
            ? ProgressErrors.IssueProgressNotFound()
            : issueProgress;
    }

    public async Task<int> DeleteByIssueIdAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        // IssueSubmission cascades via FK (ON DELETE CASCADE) from IssueProgress
        const string sql = "DELETE FROM issue_progress WHERE issue_id = @IssueId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { IssueId = issueId },
                cancellationToken: cancellationToken));
    }
}
