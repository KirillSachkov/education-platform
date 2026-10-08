using AccessService.Core.Database;
using AccessService.Domain.Integrations.GitHub;

namespace AccessService.Infrastructure.Postgres;

public sealed class AuthorGithubInstallationsRepository : IAuthorGithubInstallationsRepository
{
    private readonly AccessServiceDbContext _db;

    public AuthorGithubInstallationsRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(AuthorGithubInstallation installation, CancellationToken ct = default) =>
        await _db.AuthorGithubInstallations.AddAsync(installation, ct);

    public Task<AuthorGithubInstallation?> GetByAuthorAsync(Guid authorId, CancellationToken ct = default) =>
        _db.AuthorGithubInstallations.FirstOrDefaultAsync(x => x.AuthorId == authorId, ct);

    public Task<AuthorGithubInstallation?> GetByInstallationIdAsync(long installationId, CancellationToken ct = default) =>
        _db.AuthorGithubInstallations.FirstOrDefaultAsync(x => x.InstallationId == installationId, ct);

    public Task<AuthorGithubInstallation?> GetByOrgLoginAsync(string orgLogin, CancellationToken ct = default)
    {
        string normalized = orgLogin.ToLowerInvariant();
        return _db.AuthorGithubInstallations.FirstOrDefaultAsync(x => x.OrgLogin == normalized, ct);
    }
}
