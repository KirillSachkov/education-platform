using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Notes;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class MaterialNoteRepository : IMaterialNoteRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public MaterialNoteRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task AddAsync(MaterialNote note, CancellationToken cancellationToken = default)
    {
        await _dbContext.MaterialNotes.AddAsync(note, cancellationToken);
    }

    public Task<MaterialNote?> GetByAsync(
        Expression<Func<MaterialNote, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.MaterialNotes.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public void Remove(MaterialNote note)
    {
        _dbContext.MaterialNotes.Remove(note);
    }

    public async Task<int> DeleteByMaterialIdAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM material_notes WHERE material_id = @MaterialId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { MaterialId = materialId },
                cancellationToken: cancellationToken));
    }
}
