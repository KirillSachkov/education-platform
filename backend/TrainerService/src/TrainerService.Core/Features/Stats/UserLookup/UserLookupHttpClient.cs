using Core.HttpCommunication;

namespace TrainerService.Core.Features.Stats.UserLookup;

/// <summary>
///     Narrow read-only client over AuthService <c>POST /internal/users/batch</c> — resolves display
///     name + avatar URL for a batch of platform user ids so the admin trainer-stats dashboard can show
///     who the top AI spenders are (epic #681 / #680). TrainerService does not reference
///     <c>AuthService.Contracts</c>; the local request/response records mirror
///     <c>InternalUsersBatchRequest</c> + <c>AuthUserLookupDto</c> (same convention as ECS
///     <c>AuthorLookupHttpClient</c>) and System.Text.Json silently ignores the unmatched fields
///     (email, telegram, …) so they never leak. Soft-degrades to an empty dict on any AuthService
///     failure — logs a warning, never throws.
/// </summary>
internal sealed class UserLookupHttpClient : BaseHttpClient, IUserLookupClient
{
    private const string SERVICE_NAME = "AuthService";

    public UserLookupHttpClient(HttpClient httpClient, ILogger<UserLookupHttpClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public async Task<IReadOnlyDictionary<Guid, UserLookupDto>> GetUsersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, UserLookupDto>();

        InternalUsersBatchRequestBody body = new(ids.Distinct().ToArray());

        Result<List<AuthUserLookupBody>, Error> result;
        try
        {
            result = await PostAsync<InternalUsersBatchRequestBody, List<AuthUserLookupBody>>(
                "/internal/users/batch", body, ct);
        }
        catch (Exception ex) when (string.Equals(ex.GetType().Name, "BrokenCircuitException", StringComparison.Ordinal))
        {
            // Polly circuit open — matched by simple name to avoid a v7/v8 extern alias (same approach
            // as ECS CachedAuthorLookupClient). Degrade so the dashboard renders without names.
            Logger.LogWarning(ex, "AuthService circuit open for {Count} user ids. Returning ids only.", ids.Count);
            return new Dictionary<Guid, UserLookupDto>();
        }

        if (result.IsFailure)
        {
            // BaseHttpClient already logged the transport/HTTP failure; warn + degrade so the admin
            // dashboard shows top users by id without names rather than 500-ing the whole request.
            Logger.LogWarning(
                "AuthService user lookup failed for {Count} ids: {Error}. Returning ids only.",
                ids.Count, result.Error.GetMessage());
            return new Dictionary<Guid, UserLookupDto>();
        }

        Dictionary<Guid, UserLookupDto> map = new(result.Value.Count);
        foreach (AuthUserLookupBody user in result.Value)
        {
            // Display name falls back to username (platform-wide rule `name = DisplayName ?? UserName`).
            string? displayName = !string.IsNullOrWhiteSpace(user.Name) ? user.Name : user.Username;

            // AuthService returns the avatar file id; build the origin-relative FileService content URL
            // (`/api/files/{id}/content`, the canonical fileImageSrc convention). TrainerService has no
            // FileService client and the FE consumes a ready-to-render URL. null when the user has none.
            string? avatarUrl = user.AvatarId is { } avatarId
                ? $"/api/files/{avatarId:D}/content"
                : null;

            map[user.UserId] = new UserLookupDto(displayName, avatarUrl);
        }

        return map;
    }

    private sealed record InternalUsersBatchRequestBody(IReadOnlyList<Guid> UserIds);

    // Declare ONLY the fields the dashboard needs. AuthService's response also carries email +
    // telegram — deliberately NOT declared here so they can never be mapped into the admin DTO.
    private sealed record AuthUserLookupBody(
        Guid UserId,
        string? Name,
        string? Username,
        Guid? AvatarId);
}
