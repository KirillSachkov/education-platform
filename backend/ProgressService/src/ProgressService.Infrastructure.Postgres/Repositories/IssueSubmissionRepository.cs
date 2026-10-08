using System.Linq.Expressions;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.IssueSubmissions;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class IssueSubmissionRepository : IIssueSubmissionRepository
{
    private readonly ProgressDbContext _dbContext;

    public IssueSubmissionRepository(ProgressDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(IssueSubmission submission, CancellationToken cancellationToken = default)
    {
        await _dbContext.IssueSubmissions.AddAsync(submission, cancellationToken);
    }

    public async Task<Result<IssueSubmission, Error>> GetByAsync(
        Expression<Func<IssueSubmission, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        IssueSubmission? submission = await _dbContext.IssueSubmissions
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return submission is null
            ? ProgressErrors.IssueSubmissionNotFound()
            : submission;
    }

    public async Task<Result<int, Error>> GetMaxAttemptNumberAsync(Guid issueProgressId, CancellationToken cancellationToken = default)
    {
        int? max = await _dbContext.IssueSubmissions
            .Where(x => x.IssueProgressId == issueProgressId)
            .Select(x => (int?)EF.Property<int>(x, nameof(IssueSubmission.AttemptNumber)))
            .MaxAsync(cancellationToken);

        return max ?? 0;
    }
}
