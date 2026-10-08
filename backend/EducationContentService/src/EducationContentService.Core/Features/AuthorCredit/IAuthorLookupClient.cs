namespace EducationContentService.Core.Features.AuthorCredit;

/// <summary>
///     Read-only client over AuthService's <c>POST /internal/users/batch</c>. Resolves
///     author display name + avatar id for a batch of platform user ids, so ECS can
///     attribute courses/materials to their author (issue #569).
/// </summary>
/// <remarks>
///     Narrow ECS-local client — distinct from the full
///     <c>AuthService.Contracts.HttpCommunication.IAuthServiceClient</c>. This one covers
///     only author-credit enrichment. Soft-degrades to an empty dictionary on AuthService
///     outage so catalog/detail never block on missing author names.
/// </remarks>
public interface IAuthorLookupClient
{
    Task<Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>> GetAuthorsByIdsAsync(
        IReadOnlyCollection<Guid> authorIds,
        CancellationToken ct);
}
