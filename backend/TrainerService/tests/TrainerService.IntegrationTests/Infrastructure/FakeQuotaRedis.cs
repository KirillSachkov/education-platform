using System.Collections.Concurrent;
using NSubstitute;
using StackExchange.Redis;

namespace TrainerService.IntegrationTests.Infrastructure;

/// <summary>
///     A minimal in-memory <see cref="IConnectionMultiplexer"/> for the quota tests (#614 C2). The
///     production <see cref="ContentAccess.Redis"/> path is faked elsewhere (entitlements), but the
///     <c>TrainerQuotaService</c> talks to Redis directly via <c>StringIncrementAsync</c> /
///     <c>KeyExpireAsync</c> / <c>StringDecrementAsync</c>, so the quota counter must actually COUNT
///     for a cap test to trip. This backs exactly those three methods with a concurrent dictionary
///     (EXPIRE is a no-op — tests don't advance the clock) and leaves the rest of the huge
///     <see cref="IDatabase"/> surface as NSubstitute defaults (unused by the quota service).
///     <para>
///         Created once and registered as the DI singleton; <see cref="Reset"/> clears the counters
///         between tests (called from <c>ResetDatabaseAsync</c>) so a per-user-per-day counter from one
///         test doesn't leak into the next.
///     </para>
/// </summary>
public sealed class FakeQuotaRedis
{
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);

    public IConnectionMultiplexer Multiplexer { get; }

    public FakeQuotaRedis()
    {
        // Build the inner counting IDatabase substitute FIRST (it configures its own .Returns), then
        // wire it into GetDatabase — nesting the two .Returns calls confuses NSubstitute's last-call tracking.
        IDatabase database = CreateCountingDatabase();
        IConnectionMultiplexer multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        Multiplexer = multiplexer;
    }

    /// <summary>Clears all counters — per-test isolation, called from <c>ResetDatabaseAsync</c>.</summary>
    public void Reset() => _counters.Clear();

    private IDatabase CreateCountingDatabase()
    {
        IDatabase database = Substitute.For<IDatabase>();

        database
            .StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                string key = call.ArgAt<RedisKey>(0).ToString();
                long by = call.ArgAt<long>(1);
                return _counters.AddOrUpdate(key, by, (_, current) => current + by);
            });

        database
            .StringDecrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                string key = call.ArgAt<RedisKey>(0).ToString();
                long by = call.ArgAt<long>(1);
                return _counters.AddOrUpdate(key, -by, (_, current) => current - by);
            });

        // Peek (no increment) — backs ReadCountAsync, used by the VOICE pre-gate + the limits snapshot
        // (#663). Missing key ⇒ RedisValue.Null (TryParse → 0). Without this NSubstitute returns a default
        // RedisValue and every read would look like 0 used, so the minute-budget could never trip.
        database
            .StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                string key = call.ArgAt<RedisKey>(0).ToString();
                return _counters.TryGetValue(key, out long value) ? (RedisValue)value : RedisValue.Null;
            });

        // EXPIRE is a no-op in tests (we never advance the clock past a window boundary).
        database
            .KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<CommandFlags>())
            .Returns(true);

        return database;
    }
}
