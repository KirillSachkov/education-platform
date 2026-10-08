using System.Linq.Expressions;
using AccessService.Core.Database;
using AccessService.Domain;

namespace AccessService.Infrastructure.Postgres;

internal sealed class InviteLinksRepository : IInviteLinksRepository
{
    private readonly AccessServiceDbContext _db;

    public InviteLinksRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(InviteLink invite, CancellationToken ct = default) =>
        await _db.InviteLinks.AddAsync(invite, ct);

    public async Task<Result<InviteLink, Error>> GetByAsync(
        Expression<Func<InviteLink, bool>> predicate,
        CancellationToken ct = default)
    {
        InviteLink? invite = await _db.InviteLinks.FirstOrDefaultAsync(predicate, ct);
        return invite is null ? AccessErrors.InviteNotFound() : invite;
    }

    public async Task<IReadOnlyList<InviteLink>> GetManyByAsync(
        Expression<Func<InviteLink, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.InviteLinks.Where(predicate).ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        Expression<Func<InviteLink, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.InviteLinks.AnyAsync(predicate, ct);

    public void Remove(InviteLink invite) => _db.InviteLinks.Remove(invite);
}
