using System.Linq.Expressions;
using AuthService.Core.Database;
using AuthService.Domain.AuthorSpaces;

namespace AuthService.Infrastructure.Postgres.Repositories;

public sealed class AuthorSpaceRepository : IAuthorSpaceRepository
{
    private readonly AuthDbContext _context;

    public AuthorSpaceRepository(AuthDbContext context) => _context = context;

    public async Task<AuthorSpace?> GetByAsync(
        Expression<Func<AuthorSpace, bool>> predicate,
        CancellationToken ct) =>
        await _context.AuthorSpaces.FirstOrDefaultAsync(predicate, ct);

    public async Task<List<AuthorSpace>> GetManyByAsync(
        Expression<Func<AuthorSpace, bool>> predicate,
        CancellationToken ct) =>
        await _context.AuthorSpaces.Where(predicate).ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        Expression<Func<AuthorSpace, bool>> predicate,
        CancellationToken ct) =>
        await _context.AuthorSpaces.AnyAsync(predicate, ct);

    public async Task AddAsync(AuthorSpace space, CancellationToken ct) =>
        await _context.AuthorSpaces.AddAsync(space, ct);
}
