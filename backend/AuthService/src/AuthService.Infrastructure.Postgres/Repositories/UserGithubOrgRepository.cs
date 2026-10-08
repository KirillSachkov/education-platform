using AuthService.Core.Database;
using Npgsql;

namespace AuthService.Infrastructure.Postgres.Repositories;

public sealed class UserGithubOrgRepository : IUserGithubOrgRepository
{
    private readonly AuthDbContext _context;

    public UserGithubOrgRepository(AuthDbContext context) => _context = context;

    public async Task<IReadOnlyList<string>> GetByUserAsync(Guid userId, CancellationToken ct) =>
        await _context.UserGithubOrgs
            .Where(x => x.UserId == userId)
            .Select(x => x.OrgSlug)
            .ToListAsync(ct);

    public async Task ReplaceAllAsync(
        Guid userId,
        IReadOnlyCollection<string> orgSlugs,
        DateTime syncedAt,
        CancellationToken ct)
    {
        string[] normalized = orgSlugs
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Атомарный full reconcile — UPSERT и DELETE в одном SQL-стейтменте.
        // Data-modifying CTE в Postgres исполняется всегда, между ними не может
        // пройти конкурентный запрос того же юзера, так что DELETE не вынесет
        // только что вставленные строки.
        // NOT EXISTS вместо NOT IN — корректно работает для пустого CTE
        // (NOT EXISTS на пустом сете = TRUE → удалит все orgs у юзера).
        await _context.Database.ExecuteSqlRawAsync(
            """
            WITH upserted AS (
                INSERT INTO user_github_orgs (user_id, org_slug, synced_at)
                SELECT @userId, slug, @syncedAt FROM unnest(@slugs) AS slug
                ON CONFLICT (user_id, org_slug) DO UPDATE SET synced_at = EXCLUDED.synced_at
                RETURNING org_slug
            )
            DELETE FROM user_github_orgs t
            WHERE t.user_id = @userId
              AND NOT EXISTS (SELECT 1 FROM upserted u WHERE u.org_slug = t.org_slug)
            """,
            [
                new NpgsqlParameter("@userId", userId),
                new NpgsqlParameter("@syncedAt", syncedAt),
                new NpgsqlParameter("@slugs", normalized) { DataTypeName = "text[]" }
            ],
            ct);
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsByOrgAsync(string orgSlug, CancellationToken ct)
    {
        string normalized = orgSlug.ToLowerInvariant();

        return await _context.UserGithubOrgs
            .AsNoTracking()
            .Where(x => x.OrgSlug == normalized)
            .Select(x => x.UserId)
            .ToListAsync(ct);
    }
}
