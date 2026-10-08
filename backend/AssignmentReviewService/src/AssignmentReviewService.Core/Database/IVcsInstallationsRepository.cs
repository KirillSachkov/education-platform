using System.Linq.Expressions;
using AssignmentReviewService.Domain.Vcs;

namespace AssignmentReviewService.Core.Database;

public interface IVcsInstallationsRepository
{
    Task AddAsync(VcsInstallation installation, CancellationToken ct = default);

    Task<VcsInstallation?> GetByAsync(
        Expression<Func<VcsInstallation, bool>> predicate, CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<VcsInstallation, bool>> predicate, CancellationToken ct = default);

    Task<IReadOnlyList<VcsInstallation>> GetManyByAsync(
        Expression<Func<VcsInstallation, bool>> predicate, CancellationToken ct = default);
}
