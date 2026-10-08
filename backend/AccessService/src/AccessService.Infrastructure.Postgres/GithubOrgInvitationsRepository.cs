using AccessService.Core.Database;
using AccessService.Domain.Integrations.GitHub;

namespace AccessService.Infrastructure.Postgres;

public sealed class GithubOrgInvitationsRepository : IGithubOrgInvitationsRepository
{
    private readonly AccessServiceDbContext _db;

    public GithubOrgInvitationsRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(GithubOrgInvitation invitation, CancellationToken ct = default) =>
        await _db.GithubOrgInvitations.AddAsync(invitation, ct);

    public Task<GithubOrgInvitation?> GetByUserPlanAsync(Guid userId, Guid planId, CancellationToken ct = default) =>
        _db.GithubOrgInvitations.FirstOrDefaultAsync(
            x => x.UserId == userId && x.PlanId == planId, ct);

    public async Task<IReadOnlyList<GithubOrgInvitation>> GetActiveMembershipsByUserAsync(
        Guid userId, CancellationToken ct = default) =>
        await _db.GithubOrgInvitations
            .Where(x => x.UserId == userId
                && (x.Status == GithubInvitationStatus.ACCEPTED
                    || x.Status == GithubInvitationStatus.PENDING))
            .ToListAsync(ct);

    public Task<GithubOrgInvitation?> GetPendingByOrgLoginAsync(
        string orgLogin, string githubLogin, CancellationToken ct = default)
    {
        string normalizedOrg = orgLogin.ToLowerInvariant();
        string normalizedLogin = githubLogin.ToLowerInvariant();
        return _db.GithubOrgInvitations.FirstOrDefaultAsync(
            x => x.OrgLogin == normalizedOrg
                && x.GithubLogin == normalizedLogin
                && x.Status == GithubInvitationStatus.PENDING,
            ct);
    }

    public Task<GithubOrgInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.GithubOrgInvitations.FirstOrDefaultAsync(x => x.Id == id, ct);
}
