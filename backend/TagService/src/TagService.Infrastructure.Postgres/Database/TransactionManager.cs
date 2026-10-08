using System.Data.Common;
using Core.Database;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using TagService.Domain;
using TagService.Infrastructure.Postgres.Configurations;
using Wolverine.EntityFrameworkCore;

namespace TagService.Infrastructure.Postgres.Database;

public sealed class TransactionManager : ITransactionManager, IDisposable
{
    // Exposed as public so the integration-test factory can pass the same map to
    // TestTransactionManager, keeping test behaviour identical to production.
    public static readonly IReadOnlyDictionary<string, Func<Error>> UniqueConstraintMap =
        new Dictionary<string, Func<Error>>(StringComparer.OrdinalIgnoreCase)
        {
            { TagsIndex.SLUG_UNIQUE, TagErrors.TagAlreadyExists },
            { TagsIndex.AUTHOR_SLUG_UNIQUE, TagErrors.TagAlreadyExists },
            { TagAliasesIndex.TAG_ALIAS_UNIQUE, TagErrors.AliasAlreadyAttached },
            { TagAliasesIndex.ALIAS_TAG_UNIQUE, TagErrors.AliasAlreadyAttached },
            { EntityTagsIndex.ENTITY_TAG_UNIQUE, TagErrors.EntityTagAlreadyAttached }
        };

    private static readonly IReadOnlyDictionary<string, Func<Error>> _uniqueConstraintToErrorMap = UniqueConstraintMap;

    private readonly IDbContextOutbox<TagDbContext> _outbox;
    private readonly ILogger<TransactionManager> _logger;
    private IDbContextTransaction? _currentTransaction;

    public TransactionManager(
        IDbContextOutbox<TagDbContext> outbox,
        ILogger<TransactionManager> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _currentTransaction = await _outbox.DbContext.Database.BeginTransactionAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to begin transaction");
            return GeneralErrors.DatabaseError();
        }
    }

    public async Task<UnitResult<Error>> CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
            return GeneralErrors.DatabaseError();

        try
        {
            await _outbox.DbContext.SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);

            // The durable envelope is persisted by SaveChanges inside the database
            // transaction. Sending it immediately is an optimization only; Wolverine's
            // polling relay will deliver it after a transient flush failure.
            try
            {
                await _outbox.FlushOutgoingMessagesAsync();
            }
            catch (Exception flushException)
            {
                _logger.LogWarning(
                    flushException,
                    "Outbox flush failed after commit; the durable relay will retry delivery.");
            }

            return UnitResult.Success<Error>();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict during commit");
            await RollbackAsync(cancellationToken);
            return GeneralErrors.ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (TryExtractPostgresException(ex, out PostgresException? pgEx))
        {
            PostgresException postgresException = pgEx!;
            _logger.LogWarning(ex, "Postgres update error during commit: {Constraint}", postgresException.ConstraintName);
            await RollbackAsync(cancellationToken);
            return HandlePostgresException(postgresException);
        }
        catch (PostgresException ex)
        {
            _logger.LogWarning(ex, "Postgres error during commit: {Constraint}", ex.ConstraintName);
            await RollbackAsync(cancellationToken);
            return HandlePostgresException(ex);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Operation cancelled during commit");
            await RollbackAsync(cancellationToken);
            return GeneralErrors.OperationCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during commit");
            await RollbackAsync(cancellationToken);
            return GeneralErrors.DatabaseError();
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeTransactionAsync();
    }

    public void Dispose()
    {
        if (_currentTransaction is not null)
        {
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }
    }

    public DbConnection GetDbConnection() => _outbox.DbContext.Database.GetDbConnection();

    public async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction is not null)
            {
                await _outbox.DbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }

            return UnitResult.Success<Error>();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict during save");
            return GeneralErrors.ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (TryExtractPostgresException(ex, out PostgresException? pgEx))
        {
            PostgresException postgresException = pgEx!;
            _logger.LogWarning(ex, "Postgres update error during save: {Constraint}", postgresException.ConstraintName);
            return HandlePostgresException(postgresException);
        }
        catch (PostgresException ex)
        {
            _logger.LogWarning(ex, "Postgres error during save: {Constraint}", ex.ConstraintName);
            return HandlePostgresException(ex);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Operation cancelled during save");
            return GeneralErrors.OperationCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during save");
            return GeneralErrors.DatabaseError();
        }
    }

    private async Task RollbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_currentTransaction is not null)
                await _currentTransaction.RollbackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback transaction");
        }
    }

    private Error HandlePostgresException(PostgresException pgEx)
    {
        if (string.Equals(pgEx.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal) &&
            TryMapUniqueViolation(pgEx.ConstraintName, out Error error))
        {
            _logger.LogWarning("Mapped unique constraint violation: {Constraint}", pgEx.ConstraintName);
            return error;
        }

        _logger.LogError(pgEx, "Database error: {SqlState}", pgEx.SqlState);
        return GeneralErrors.DatabaseError();
    }

    private static bool TryExtractPostgresException(DbUpdateException ex, out PostgresException? pgEx)
    {
        pgEx = ex.InnerException as PostgresException;
        return pgEx is not null;
    }

    private static bool TryMapUniqueViolation(string? constraintName, out Error error)
    {
        if (!string.IsNullOrWhiteSpace(constraintName) &&
            _uniqueConstraintToErrorMap.TryGetValue(constraintName, out Func<Error>? factory))
        {
            error = factory();
            return true;
        }

        error = default!;
        return false;
    }

    private async Task DisposeTransactionAsync()
    {
        if (_currentTransaction is not null)
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }
}
