using System.Data.Common;
using Core.Database;
using Dapper;
using NotificationService.Core.Database;

namespace NotificationService.Infrastructure.Postgres.Digest;

/// <summary>
/// Атомарный claim дедупа доставки дайджеста через <c>INSERT ... ON CONFLICT DO NOTHING</c>.
/// PK <c>(correlation_id, inbox_hash)</c> гарантирует, что ровно один аккаунт физического
/// инбокса «займёт» пару — race-safe между репликами и одновременными scheduled+manual
/// проходами (rowcount == 1 → заняли первыми).
/// </summary>
public sealed class DigestEmailDedupStore : IDigestEmailDedupStore
{
    private const int QUERY_TIMEOUT_SECONDS = 30;

    private readonly ITransactionManager _transactions;

    public DigestEmailDedupStore(ITransactionManager transactions) => _transactions = transactions;

    public async Task<bool> TryClaimInboxAsync(Guid correlationId, byte[] inboxHash, CancellationToken ct = default)
    {
        DbConnection conn = _transactions.GetDbConnection();

        int inserted = await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO notifications.digest_email_dedup (correlation_id, inbox_hash)
            VALUES (@correlationId, @inboxHash)
            ON CONFLICT DO NOTHING
            """,
            new { correlationId, inboxHash },
            commandTimeout: QUERY_TIMEOUT_SECONDS,
            cancellationToken: ct));

        return inserted == 1;
    }
}
