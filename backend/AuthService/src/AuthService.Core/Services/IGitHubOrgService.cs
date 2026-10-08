namespace AuthService.Core.Services;

public interface IGitHubOrgService
{
    Task<Result<bool, Error>> IsMemberOfOrgAsync(
        string accessToken,
        string orgSlug,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Возвращает полный список org-slug'ов пользователя через <c>GET /user/orgs</c>.
    ///     Slug'и приходят в lowercase. Прозрачно пагинирует до 1000 оргов.
    /// </summary>
    Task<Result<IReadOnlyList<string>, Error>> FetchUserOrgsAsync(
        string accessToken,
        CancellationToken cancellationToken);
}
