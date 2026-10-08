using AccessService.Domain.Integrations.GitHub;

namespace AccessService.Core.Database;

public interface IGithubOrgInvitationsRepository
{
    Task AddAsync(GithubOrgInvitation invitation, CancellationToken ct = default);

    Task<GithubOrgInvitation?> GetByUserPlanAsync(Guid userId, Guid planId, CancellationToken ct = default);

    /// <summary>
    ///     Активные (ACCEPTED / PENDING) org-приглашения пользователя — те, где юзер
    ///     занимает слот в org'и (member или ожидает accept). Используется при истечении
    ///     доступа (#687), чтобы исключить юзера из org'и, доступ к которой он потерял.
    /// </summary>
    Task<IReadOnlyList<GithubOrgInvitation>> GetActiveMembershipsByUserAsync(
        Guid userId, CancellationToken ct = default);

    Task<GithubOrgInvitation?> GetPendingByOrgLoginAsync(
        string orgLogin, string githubLogin, CancellationToken ct = default);

    Task<GithubOrgInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
