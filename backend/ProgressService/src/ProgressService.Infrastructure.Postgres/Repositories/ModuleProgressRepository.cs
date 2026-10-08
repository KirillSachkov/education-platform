using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Modules;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class ModuleProgressRepository : IModuleProgressRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public ModuleProgressRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task AddAsync(ModuleProgress moduleProgress, CancellationToken cancellationToken = default)
    {
        await _dbContext.ModuleProgresses.AddAsync(moduleProgress, cancellationToken);
    }

    public async Task<Result<ModuleProgress, Error>> GetByAsync(
        Expression<Func<ModuleProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // Sначала Local: после EnsureModuleProgressAsync → AddAsync сущность
        // ещё не commit'нута в БД (SaveChanges вызывается позже), но домен-event handler
        // уже должен её увидеть в том же раунде dispatch.
        Func<ModuleProgress, bool> compiled = predicate.Compile();
        ModuleProgress? tracked = _dbContext.ModuleProgresses.Local.FirstOrDefault(compiled);
        if (tracked is not null)
        {
            return tracked;
        }

        ModuleProgress? moduleProgress = await _dbContext.ModuleProgresses
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return moduleProgress is null
            ? ProgressErrors.ModuleProgressNotFound()
            : moduleProgress;
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<ModuleProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Func<ModuleProgress, bool> compiled = predicate.Compile();
        if (_dbContext.ModuleProgresses.Local.Any(compiled))
        {
            return true;
        }

        return await _dbContext.ModuleProgresses.AnyAsync(predicate, cancellationToken);
    }

    public async Task<int> DeleteByModuleIdAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM module_progress WHERE module_id = @ModuleId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { ModuleId = moduleId },
                cancellationToken: cancellationToken));
    }
}
