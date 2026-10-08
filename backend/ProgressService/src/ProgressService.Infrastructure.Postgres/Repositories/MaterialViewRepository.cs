using System.Data.Common;
using System.Linq.Expressions;
using Core.Database;
using Dapper;
using Microsoft.EntityFrameworkCore.Storage;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Materials;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class MaterialViewRepository : IMaterialViewRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public MaterialViewRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task AddAsync(MaterialView materialView, CancellationToken cancellationToken = default)
    {
        await _dbContext.MaterialViews.AddAsync(materialView, cancellationToken);
    }

    public async Task<Result<MaterialView, Error>> GetByAsync(
        Expression<Func<MaterialView, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        MaterialView? materialView = await _dbContext.MaterialViews
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return materialView is null
            ? ProgressErrors.MaterialViewNotFound()
            : materialView;
    }

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetCompletedMaterialMapAsync(
        Guid userId,
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default)
    {
        if (materialIds.Count == 0)
        {
            return new Dictionary<Guid, DateTime>();
        }

        // Только явно отмеченные материалы (is_completed=true): silent track-view'ы НЕ
        // попадают в "изучено" — issue #285. CompletedAt не nullable если is_completed=true.
        const string sql = """
                           SELECT material_id AS MaterialId, COALESCE(completed_at, viewed_at) AS ViewedAt
                           FROM material_views
                           WHERE user_id = @UserId
                             AND material_id = ANY(@MaterialIds)
                             AND is_completed = TRUE;
                           """;

        IEnumerable<(Guid MaterialId, DateTime ViewedAt)> rows = await _transactionManager
            .GetDbConnection()
            .QueryAsync<(Guid MaterialId, DateTime ViewedAt)>(
                new CommandDefinition(
                    sql,
                    new { UserId = userId, MaterialIds = materialIds.ToArray() },
                    cancellationToken: cancellationToken));

        return rows.ToDictionary(x => x.MaterialId, x => x.ViewedAt);
    }

    public async Task<bool> TryInsertTrackAsync(
        Guid userId,
        Guid materialId,
        CancellationToken cancellationToken = default)
    {
        // ON CONFLICT DO NOTHING — идемпотентно для пары (user_id, material_id).
        // Существующая строка (любого is_completed) сохраняется без изменений: silent
        // track НЕ должен понизить ранее проставленный is_completed=true. Issue #285.
        const string sql = """
                           INSERT INTO material_views
                               (id, user_id, material_id, viewed_at, created_at, is_completed, completed_at)
                           VALUES (@Id, @UserId, @MaterialId, @ViewedAt, @CreatedAt, FALSE, NULL)
                           ON CONFLICT (user_id, material_id) DO NOTHING
                           RETURNING id;
                           """;

        DateTime now = DateTime.UtcNow;
        var parameters = new
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MaterialId = materialId,
            ViewedAt = now,
            CreatedAt = now,
        };

        Guid? inserted = await _transactionManager.GetDbConnection()
            .ExecuteScalarAsync<Guid?>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return inserted is not null;
    }

    public async Task<Result<MaterialViewCompletionResult, Error>> CompleteAsync(
        Guid userId,
        Guid materialId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           WITH upsert AS (
                               INSERT INTO material_views
                                   (id, user_id, material_id, viewed_at, created_at, is_completed, completed_at)
                               VALUES (@Id, @UserId, @MaterialId, @Now, @Now, TRUE, @Now)
                               ON CONFLICT (user_id, material_id) DO UPDATE
                               SET is_completed = TRUE,
                                   completed_at = COALESCE(material_views.completed_at, @Now)
                               WHERE material_views.is_completed = FALSE
                               RETURNING viewed_at AS "ViewedAt", TRUE AS "StateChanged"
                           )
                           SELECT "ViewedAt", "StateChanged"
                           FROM upsert
                           UNION ALL
                           SELECT viewed_at AS "ViewedAt", FALSE AS "StateChanged"
                           FROM material_views
                           WHERE user_id = @UserId
                             AND material_id = @MaterialId
                             AND NOT EXISTS (SELECT 1 FROM upsert)
                           LIMIT 1;
                           """;

        DateTime now = DateTime.UtcNow;
        var parameters = new
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MaterialId = materialId,
            Now = now
        };

        DbTransaction? transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        MaterialViewCompletionResult? result = await _transactionManager.GetDbConnection()
            .QuerySingleOrDefaultAsync<MaterialViewCompletionResult>(
                new CommandDefinition(
                    sql,
                    parameters,
                    transaction,
                    cancellationToken: cancellationToken));

        return result is null
            ? ProgressErrors.MaterialViewNotFound()
            : result;
    }

    public void Remove(MaterialView materialView)
    {
        _dbContext.MaterialViews.Remove(materialView);
    }

    public async Task<int> DeleteByMaterialIdAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM material_views WHERE material_id = @MaterialId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { MaterialId = materialId },
                cancellationToken: cancellationToken));
    }
}
