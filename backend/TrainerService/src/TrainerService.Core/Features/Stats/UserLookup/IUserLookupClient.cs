namespace TrainerService.Core.Features.Stats.UserLookup;

/// <summary>
///     Read-only batch lookup of platform user display credit (name + avatar URL) over AuthService
///     <c>POST /internal/users/batch</c>. Re-introduces TrainerService → AuthService S2S (the only
///     cross-service HTTP the trainer makes). Implementations <b>soft-degrade and never throw</b>:
///     on AuthService outage they return an empty / partial dictionary so user enrichment never
///     blocks the admin stats endpoint (it just falls back to raw ids).
/// </summary>
public interface IUserLookupClient
{
    Task<IReadOnlyDictionary<Guid, UserLookupDto>> GetUsersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct);
}
