using AccessService.Domain.Integrations.GitHub;

namespace AccessService.Core.Database;

public interface IAuthorGithubInstallationsRepository
{
    Task AddAsync(AuthorGithubInstallation installation, CancellationToken ct = default);

    Task<AuthorGithubInstallation?> GetByAuthorAsync(Guid authorId, CancellationToken ct = default);

    Task<AuthorGithubInstallation?> GetByInstallationIdAsync(long installationId, CancellationToken ct = default);

    Task<AuthorGithubInstallation?> GetByOrgLoginAsync(string orgLogin, CancellationToken ct = default);
}
