using Core.HttpCommunication;

namespace EducationContentService.Core.Features.AuthorCredit;

internal sealed class AuthorLookupHttpClient : BaseHttpClient, IAuthorLookupClient
{
    private const string SERVICE_NAME = "AuthService";

    public AuthorLookupHttpClient(
        HttpClient httpClient,
        ILogger<AuthorLookupHttpClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public async Task<Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>> GetAuthorsByIdsAsync(
        IReadOnlyCollection<Guid> authorIds,
        CancellationToken ct)
    {
        if (authorIds.Count == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(
                new Dictionary<Guid, AuthorCreditDto>());
        }

        // Local request/response shapes mirror AuthService's InternalUsersBatchRequest +
        // AuthUserLookupDto — ECS does not reference AuthService.Contracts (narrow client,
        // same convention as CoursePricingHttpClient vs AccessService.Contracts).
        InternalUsersBatchRequestBody body = new(authorIds.Distinct().ToArray());

        Result<List<AuthUserLookupBody>, Error> result =
            await PostAsync<InternalUsersBatchRequestBody, List<AuthUserLookupBody>>(
                "/internal/users/batch",
                body,
                ct);

        if (result.IsFailure)
            return result.Error;

        Dictionary<Guid, AuthorCreditDto> map = new(result.Value.Count);
        foreach (AuthUserLookupBody user in result.Value)
        {
            // Display name falls back to username (mirrors the platform-wide rule
            // `name = DisplayName ?? UserName`) so the credit is never blank.
            string? displayName = !string.IsNullOrWhiteSpace(user.Name)
                ? user.Name
                : user.Username;

            map[user.UserId] = new AuthorCreditDto(user.UserId, displayName, user.AvatarId);
        }

        return Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(map);
    }

    private sealed record InternalUsersBatchRequestBody(IReadOnlyList<Guid> UserIds);

    // Narrow boundary (#569 security review): declare ONLY the fields ECS needs for the
    // public byline. AuthService's response also carries `email` — deliberately NOT declared
    // here so it can never be accidentally mapped onto the outward (anonymous-readable) credit.
    // System.Text.Json silently ignores the unmatched JSON property.
    private sealed record AuthUserLookupBody(
        Guid UserId,
        string? Name,
        string? Username,
        Guid? AvatarId);
}
