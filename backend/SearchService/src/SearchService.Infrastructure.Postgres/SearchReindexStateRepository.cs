using Npgsql;
using SearchService.Core.Reindex.State;

namespace SearchService.Infrastructure.Postgres;

public sealed class SearchReindexStateRepository : ISearchReindexStateRepository
{
    // PostgreSQL SQLSTATE 23505 = unique_violation. На singleton-таблице срабатывает,
    // если два процесса/handler'а одновременно INSERT'ят начальную строку.
    private const string PG_UNIQUE_VIOLATION = "23505";

    private readonly SearchDbContext _db;

    public SearchReindexStateRepository(SearchDbContext db)
    {
        _db = db;
    }

    public async Task<SearchReindexState> GetOrInitAsync(CancellationToken cancellationToken = default)
    {
        SearchReindexState? existing = await _db.ReindexStates
            .FirstOrDefaultAsync(s => s.Id == SearchReindexState.SINGLETON_ID, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        SearchReindexState initial = SearchReindexState.Initial();
        await _db.ReindexStates.AddAsync(initial, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return initial;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg
            && string.Equals(pg.SqlState, PG_UNIQUE_VIOLATION, StringComparison.Ordinal))
        {
            // Race с другим процессом, который INSERT'нул строку первым.
            // Откатываем ChangeTracker и читаем то, что вставил соперник.
            _db.Entry(initial).State = EntityState.Detached;

            SearchReindexState? raced = await _db.ReindexStates
                .FirstOrDefaultAsync(s => s.Id == SearchReindexState.SINGLETON_ID, cancellationToken);

            return raced
                ?? throw new InvalidOperationException(
                    "reindex_state singleton row vanished after unique-violation; check CHECK constraint integrity");
        }
    }

    public async Task MarkAppliedAsync(
        int generation,
        string? schemaHash,
        string? deployStamp,
        Guid requestId,
        DateTime appliedAtUtc,
        CancellationToken cancellationToken = default)
    {
        SearchReindexState state = await GetOrInitAsync(cancellationToken);
        state.MarkApplied(generation, schemaHash, deployStamp, requestId, appliedAtUtc);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
