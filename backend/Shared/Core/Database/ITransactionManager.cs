using System.Data.Common;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace Core.Database;

/// <summary>
/// Manages database transactions and change persistence.
/// Lifetime: Scoped (one instance per request/operation).
///
/// Usage:
/// 1. Auto mode — call SaveChangesAsync() without BeginTransactionAsync().
///    ORM/outbox handles transaction and message flushing automatically.
///
/// 2. Manual mode — call BeginTransactionAsync(), then SaveChangesAsync() one or more times,
///    then CommitTransactionAsync(). Rollback happens automatically on Dispose if not committed.
/// </summary>
public interface ITransactionManager : IAsyncDisposable
{
    /// <summary>
    /// Starts a new transaction. Throws if a transaction is already active.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<UnitResult<Error>> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes pending changes to the database.
    /// In manual mode — executes SQL within the active transaction (no commit).
    /// In auto mode — saves, commits, and flushes outbox messages in one shot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the active transaction and flushes outbox messages.
    /// Must be called after BeginTransactionAsync + SaveChangesAsync.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<UnitResult<Error>> CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the underlying database connection for raw SQL / Dapper queries.
    /// </summary>
    DbConnection GetDbConnection();
}