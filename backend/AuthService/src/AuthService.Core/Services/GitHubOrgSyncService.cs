using AuthService.Core.Database;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.Core.Services;

public sealed class GitHubOrgSyncService(
    IGitHubOrgService gitHubOrgService,
    IUserGithubOrgRepository userGithubOrgRepository,
    IOutboxService outboxService,
    ITransactionManager transactionManager,
    TimeProvider timeProvider,
    ILogger<AuthAudit> audit)
{
    /// <summary>
    ///     Тащит полный список GitHub-оргов пользователя, складывает в <c>auth.user_github_orgs</c>,
    ///     публикует <see cref="UserGithubLogin" /> со всем списком оргов. Consumer'ы
    ///     (AccessService) сами решают, какие совпадают с интересующими их сущностями
    ///     (плановыми org-slug'ами).
    /// </summary>
    public async Task<int> TrySyncAsync(
        Guid userId,
        string? username,
        string? accessToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return 0;

        Result<IReadOnlyList<string>, Error> userOrgsResult =
            await gitHubOrgService.FetchUserOrgsAsync(accessToken, ct);
        if (userOrgsResult.IsFailure)
        {
            audit.LogWarning(
                "Failed to fetch GitHub organizations for user {UserId}: {Error}",
                userId,
                userOrgsResult.Error);
            return 0;
        }

        // Нормализация (lowercase + dedup) живёт в IUserGithubOrgRepository.ReplaceAllAsync —
        // нет смысла делать её ещё и здесь.
        IReadOnlyList<string> userOrgs = userOrgsResult.Value;

        UnitResult<Error> beginResult = await transactionManager.BeginTransactionAsync(ct);
        if (beginResult.IsFailure)
            throw new InvalidOperationException("Failed to start GitHub organization sync transaction");

        await userGithubOrgRepository.ReplaceAllAsync(
            userId, userOrgs, timeProvider.GetUtcNow().UtcDateTime, ct);

        await outboxService.PublishAsync(new UserGithubLogin(userId, username, userOrgs));

        UnitResult<Error> commitResult = await transactionManager.CommitTransactionAsync(ct);
        if (commitResult.IsFailure)
            throw new InvalidOperationException("Failed to persist GitHub organization snapshot");

        return userOrgs.Count;
    }

    /// <summary>
    ///     Лёгкий ресинк по кэшу: GitHub API не дёргаем, только пере-публикуем
    ///     <see cref="UserGithubLogin" /> со всеми кэшированными orgs пользователя.
    ///     Используется XHR-эндпоинтом «Sync now» — пользователь нажал кнопку и хочет
    ///     перепроверить grants без OAuth-редиректа. Если юзер только что вступил
    ///     в новую org — нужен полный <see cref="TrySyncAsync"/> через OAuth-flow.
    /// </summary>
    /// <returns>Список cached org-slug'ов, отправленных consumer'ам.</returns>
    public async Task<IReadOnlyList<string>> SyncFromCacheAsync(
        Guid userId,
        string? username,
        CancellationToken ct)
    {
        IReadOnlyList<string> cachedOrgs = await userGithubOrgRepository.GetByUserAsync(userId, ct);

        await outboxService.PublishAsync(new UserGithubLogin(userId, username, cachedOrgs));
        UnitResult<Error> saveResult = await transactionManager.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            throw new InvalidOperationException("Failed to publish cached GitHub organization snapshot");

        return cachedOrgs;
    }
}
