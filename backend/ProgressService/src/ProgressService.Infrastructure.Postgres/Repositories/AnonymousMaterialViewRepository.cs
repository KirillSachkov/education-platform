using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class AnonymousMaterialViewRepository : IAnonymousMaterialViewRepository
{
    private readonly ITransactionManager _transactionManager;

    public AnonymousMaterialViewRepository(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<bool> UpsertAsync(
        string anonymousId,
        Guid materialId,
        CancellationToken cancellationToken = default)
    {
        // INSERT ... ON CONFLICT DO NOTHING RETURNING id — Postgres-специфичный upsert
        // без extra SELECT'а перед вставкой. Гонка двух параллельных запросов с одной парой
        // (anonymous_id, material_id) разрешается на стороне БД через UNIQUE-индекс.
        const string sql = """
                           INSERT INTO anonymous_material_views
                               (id, anonymous_id, material_id, viewed_at, created_at)
                           VALUES (@Id, @AnonymousId, @MaterialId, @ViewedAt, @CreatedAt)
                           ON CONFLICT (anonymous_id, material_id) DO NOTHING
                           RETURNING id;
                           """;

        DateTime now = DateTime.UtcNow;
        var parameters = new
        {
            Id = Guid.CreateVersion7(),
            AnonymousId = anonymousId,
            MaterialId = materialId,
            ViewedAt = now,
            CreatedAt = now,
        };

        Guid? inserted = await _transactionManager.GetDbConnection()
            .ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return inserted is not null;
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GetTotalViewsCountsAsync(
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken cancellationToken = default)
    {
        if (materialIds.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        // Считаем уникальные просмотры в обеих таблицах одним round-trip'ом через UNION ALL.
        // Каждая таблица имеет UNIQUE по (subject_id, material_id), поэтому COUNT(*) уже даёт
        // уникальные просмотры — DISTINCT не нужен. На внешнем уровне суммируем по material_id.
        const string sql = """
                           SELECT material_id AS MaterialId, SUM(cnt)::bigint AS Count
                           FROM (
                               SELECT material_id, COUNT(*) AS cnt
                               FROM material_views
                               WHERE material_id = ANY(@MaterialIds)
                               GROUP BY material_id
                               UNION ALL
                               SELECT material_id, COUNT(*) AS cnt
                               FROM anonymous_material_views
                               WHERE material_id = ANY(@MaterialIds)
                               GROUP BY material_id
                           ) AS combined
                           GROUP BY material_id;
                           """;

        IEnumerable<(Guid MaterialId, long Count)> rows = await _transactionManager
            .GetDbConnection()
            .QueryAsync<(Guid MaterialId, long Count)>(
                new CommandDefinition(
                    sql,
                    new { MaterialIds = materialIds.ToArray() },
                    cancellationToken: cancellationToken));

        return rows.ToDictionary(x => x.MaterialId, x => x.Count);
    }

    public async Task<int> DeleteByMaterialIdAsync(Guid materialId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM anonymous_material_views WHERE material_id = @MaterialId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { MaterialId = materialId },
                cancellationToken: cancellationToken));
    }
}
