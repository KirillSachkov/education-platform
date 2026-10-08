using System.Data;
using System.Data.Common;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using SharedKernel;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;
using MaterialProcessingService.Infrastructure.Postgres.Configurations;

namespace MaterialProcessingService.Infrastructure.Postgres;

public sealed class TransactionManager : ITransactionManager, IDisposable
{
    private static readonly Dictionary<string, string> _constraintToFieldMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { TimecodeGenerationJobConfiguration.ACTIVE_JOB_INDEX, "Активная генерация тайм-кодов" },
        { ContentGenerationJobConfiguration.ACTIVE_JOB_INDEX, "Активная генерация контента" },
        { "ix_video_transcripts_video_asset_version", "Транскрипция видео" },
    };

    private readonly IDbContextOutbox<MaterialProcessingServiceDbContext> _outbox;
    private readonly ILogger<TransactionManager> _logger;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    private IDbContextTransaction? _currentTransaction;

    public TransactionManager(
        IDbContextOutbox<MaterialProcessingServiceDbContext> outbox,
        ILogger<TransactionManager> logger,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _outbox = outbox;
        _logger = logger;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public void Dispose()
    {
        if (_currentTransaction is not null)
        {
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }
    }

    public async ValueTask DisposeAsync() => await DisposeTransactionAsync();

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
            UnitResult<Error> dispatchResult = await DispatchDomainEventsAsync(cancellationToken);
            if (dispatchResult.IsFailure)
                return dispatchResult;

            await _outbox.DbContext.SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
            await _outbox.FlushOutgoingMessagesAsync();

            return UnitResult.Success<Error>();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict during commit");
            await RollbackAsync(cancellationToken);
            return GeneralErrors.ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
        {
            await RollbackAsync(cancellationToken);
            return HandlePostgresException(pgEx);
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

    public DbConnection GetDbConnection()
    {
        DbConnection connection = _outbox.DbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            connection.Open();

        return connection;
    }

    public async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            UnitResult<Error> dispatchResult = await DispatchDomainEventsAsync(cancellationToken);
            if (dispatchResult.IsFailure)
                return dispatchResult;

            if (_currentTransaction is not null)
                await _outbox.DbContext.SaveChangesAsync(cancellationToken);
            else
                await _outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

            return UnitResult.Success<Error>();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict during save");
            return GeneralErrors.ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
        {
            return HandlePostgresException(pgEx);
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

    private async Task<UnitResult<Error>> DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            List<IDomainEvent> events = _outbox.DbContext.ChangeTracker
                .Entries()
                .Where(entry => entry.Entity is IHasDomainEvents)
                .SelectMany(entry =>
                {
                    var entity = (IHasDomainEvents)entry.Entity;
                    List<IDomainEvent> domainEvents = [.. entity.DomainEvents];
                    entity.ClearDomainEvents();
                    return domainEvents;
                })
                .ToList();

            if (events.Count == 0)
                return UnitResult.Success<Error>();

            foreach (IDomainEvent domainEvent in events)
            {
                UnitResult<Error> result = await _domainEventDispatcher.DispatchAsync(domainEvent, cancellationToken);
                if (result.IsFailure)
                    return result;
            }
        }
    }

    private Error HandlePostgresException(PostgresException pgEx)
    {
        if (string.Equals(pgEx.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
        {
            DetachAddedEntriesAfterUniqueViolation(_outbox.DbContext);
            string fieldName = GetFieldNameFromConstraint(pgEx.ConstraintName);
            _logger.LogWarning(
                pgEx,
                "Unique constraint violation during persistence: {Constraint} -> {Field}",
                pgEx.ConstraintName,
                fieldName);

            return GeneralErrors.UniqueConstraintViolation(fieldName);
        }

        _logger.LogError(pgEx, "Database error: {SqlState}", pgEx.SqlState);
        return GeneralErrors.DatabaseError();
    }

    internal static void DetachAddedEntriesAfterUniqueViolation(DbContext dbContext)
    {
        // PostgreSQL rejected the whole INSERT statement. Keeping Added aggregates and
        // Wolverine outbox envelopes tracked would make every later SaveChanges in the
        // same scope retry the losing insert (notably transcript concurrency recovery).
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in dbContext.ChangeTracker
                     .Entries()
                     .Where(entry => entry.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string GetFieldNameFromConstraint(string? constraintName)
    {
        if (string.IsNullOrWhiteSpace(constraintName))
            return "Поле";

        return _constraintToFieldMap.TryGetValue(constraintName, out string? fieldName)
            ? fieldName
            : "Поле";
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

    private async Task DisposeTransactionAsync()
    {
        if (_currentTransaction is not null)
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }
}
