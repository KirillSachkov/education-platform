using System.Linq.Expressions;
using AuthService.Core.Database;
using AuthService.Domain;
using Npgsql;

namespace AuthService.Infrastructure.Postgres.Repositories;

/// <summary>
///     Реализация <see cref="IProfileRepository" />.
/// </summary>
public sealed class ProfileRepository : IProfileRepository
{
    private readonly AuthDbContext _context;

    public ProfileRepository(AuthDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task AddAsync(UserProfile profile, CancellationToken ct) =>
        await _context.UserProfiles.AddAsync(profile, ct);

    /// <inheritdoc />
    public async Task<UserProfile?> GetByAsync(
        Expression<Func<UserProfile, bool>> predicate,
        CancellationToken ct) =>
        await _context.UserProfiles.FirstOrDefaultAsync(predicate, ct);

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        Expression<Func<UserProfile, bool>> predicate,
        CancellationToken ct) =>
        await _context.UserProfiles.AnyAsync(predicate, ct);

    /// <inheritdoc />
    public async Task EnsureExistsAsync(Guid userId, CancellationToken ct) =>
        await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO user_profiles (id, created_at, updated_at)
            VALUES (@userId, timezone('utc', now()), timezone('utc', now()))
            ON CONFLICT (id) DO NOTHING
            """,
            [new NpgsqlParameter("@userId", userId)],
            ct);
}
