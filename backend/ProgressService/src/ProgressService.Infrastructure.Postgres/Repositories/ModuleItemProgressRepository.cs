using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Modules;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class ModuleItemProgressRepository : IModuleItemProgressRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public ModuleItemProgressRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task AddAsync(
        ModuleItemProgress moduleItemProgress,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.ModuleItemProgresses.AddAsync(moduleItemProgress, cancellationToken);
    }

    public async Task<Result<ModuleItemProgress, Error>> GetByAsync(
        Expression<Func<ModuleItemProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ModuleItemProgress? moduleItemProgress = await _dbContext.ModuleItemProgresses
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return moduleItemProgress is null
            ? ProgressErrors.ModuleItemProgressNotFound()
            : moduleItemProgress;
    }

    public async Task<IReadOnlyList<ModuleItemProgress>> GetManyByAsync(
        Expression<Func<ModuleItemProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ModuleItemProgresses
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<ModuleItemProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ModuleItemProgresses.AnyAsync(predicate, cancellationToken);
    }

    public async Task<int> CountCompletedByEnrollmentAndModuleAsync(
        Guid enrollmentId,
        Guid moduleId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ModuleItemProgresses
            .CountAsync(
                x => x.EnrollmentId == enrollmentId
                    && x.ModuleId == moduleId
                    && x.Status == ModuleItemProgressStatus.COMPLETED,
                cancellationToken);
    }

    public async Task<int> DeleteByReferenceIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM module_item_progress WHERE reference_id = @ReferenceId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { ReferenceId = referenceId },
                cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteByModuleIdAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM module_item_progress WHERE module_id = @ModuleId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { ModuleId = moduleId },
                cancellationToken: cancellationToken));
    }
}
