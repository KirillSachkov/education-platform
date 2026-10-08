using System.Linq.Expressions;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Vcs;

namespace AssignmentReviewService.Infrastructure.Postgres.Repositories;

internal sealed class VcsInstallationsRepository : IVcsInstallationsRepository
{
    private readonly AssignmentReviewServiceDbContext _db;

    public VcsInstallationsRepository(AssignmentReviewServiceDbContext db) => _db = db;

    public async Task AddAsync(VcsInstallation installation, CancellationToken ct = default)
    {
        await _db.VcsInstallations.AddAsync(installation, ct);
    }

    public Task<VcsInstallation?> GetByAsync(
        Expression<Func<VcsInstallation, bool>> predicate, CancellationToken ct = default) =>
        _db.VcsInstallations.FirstOrDefaultAsync(predicate, ct);

    public Task<bool> ExistsAsync(
        Expression<Func<VcsInstallation, bool>> predicate, CancellationToken ct = default) =>
        _db.VcsInstallations.AnyAsync(predicate, ct);

    public async Task<IReadOnlyList<VcsInstallation>> GetManyByAsync(
        Expression<Func<VcsInstallation, bool>> predicate, CancellationToken ct = default) =>
        await _db.VcsInstallations
            .AsNoTracking()
            .Where(predicate)
            .OrderByDescending(x => x.InstalledAt)
            .ToListAsync(ct);
}
