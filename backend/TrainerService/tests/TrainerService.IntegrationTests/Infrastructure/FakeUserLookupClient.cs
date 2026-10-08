using TrainerService.Core.Features.Stats.UserLookup;

namespace TrainerService.IntegrationTests.Infrastructure;

/// <summary>
///     Deterministic fake for <see cref="IUserLookupClient"/> (epic #681 T2). The real client talks to
///     AuthService over HTTP; tests register this in its place so admin top-user enrichment is
///     controllable without a live AuthService. Configure <see cref="Users"/> for known ids; set
///     <see cref="ThrowOnCall"/> to prove the endpoint soft-degrades when the lookup blows up. Reset per
///     test in <see cref="IntegrationTestsWebFactory.ResetDatabaseAsync"/>.
/// </summary>
public sealed class FakeUserLookupClient : IUserLookupClient
{
    /// <summary>Credit returned for matching ids; ids absent here resolve to no credit (null name/avatar).</summary>
    public Dictionary<Guid, UserLookupDto> Users { get; } = new();

    /// <summary>When true, <see cref="GetUsersAsync"/> throws — exercises the handler's soft-degrade path.</summary>
    public bool ThrowOnCall { get; set; }

    public Task<IReadOnlyDictionary<Guid, UserLookupDto>> GetUsersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ThrowOnCall)
            throw new InvalidOperationException("AuthService unavailable (test).");

        IReadOnlyDictionary<Guid, UserLookupDto> result = ids
            .Distinct()
            .Where(Users.ContainsKey)
            .ToDictionary(id => id, id => Users[id]);

        return Task.FromResult(result);
    }

    public void Reset()
    {
        Users.Clear();
        ThrowOnCall = false;
    }
}
