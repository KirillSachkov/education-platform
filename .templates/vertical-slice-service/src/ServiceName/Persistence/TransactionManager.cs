using System.Data;
using System.Data.Common;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SharedKernel;
using SharedKernel.DomainEvents;

namespace ServiceName.Persistence;

/// <summary>
/// Stateful TransactionManager matching the platform contract
/// (<see cref="ITransactionManager"/> from <c>SachkovTech.Core</c>).
///
/// Domain events are dispatched in a loop before SaveChanges so that any chained
/// domain reactions are flushed in the same DB transaction.
///
/// To enable Wolverine durable outbox, swap <c>_dbContext.SaveChangesAsync</c>
/// for <c>_outbox.SaveChangesAndFlushMessagesAsync</c> and inject
/// <c>IDbContextOutbox&lt;ServiceNameDbContext&gt;</c> — see CommentService for the canonical
/// outbox-aware implementation.
/// </summary>
public sealed class TransactionManager : ITransactionManager, IDisposable
{
    private readonly ServiceNameDbContext _dbContext;
    private readonly ILogger<TransactionManager> _logger;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private IDbContextTransaction? _currentTransaction;

    public TransactionManager(
        ServiceNameDbContext dbContext,
        ILogger<TransactionManager> logger,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _dbContext = dbContext;
        _logger = logger;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<UnitResult<Error>> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _currentTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
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

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict during commit");
            await RollbackAsync(cancellationToken);
            return GeneralErrors.ConcurrencyConflict();
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

    public async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            UnitResult<Error> dispatchResult = await DispatchDomainEventsAsync(cancellationToken);
            if (dispatchResult.IsFailure)
                return dispatchResult;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict during save");
            return GeneralErrors.ConcurrencyConflict();
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

    public DbConnection GetDbConnection()
    {
        DbConnection connection = _dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            connection.Open();

        return connection;
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

    private async Task<UnitResult<Error>> DispatchDomainEventsAsync(CancellationToken ct)
    {
        while (true)
        {
            List<IDomainEvent> events = _dbContext.ChangeTracker
                .Entries()
                .Where(e => e.Entity is IHasDomainEvents)
                .SelectMany(e =>
                {
                    var entity = (IHasDomainEvents)e.Entity;
                    List<IDomainEvent> domainEvents = [.. entity.DomainEvents];
                    entity.ClearDomainEvents();
                    return domainEvents;
                })
                .ToList();

            if (events.Count == 0)
                return UnitResult.Success<Error>();

            foreach (IDomainEvent domainEvent in events)
            {
                UnitResult<Error> result = await _domainEventDispatcher.DispatchAsync(domainEvent, ct);
                if (result.IsFailure)
                    return result;
            }
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

    private async Task DisposeTransactionAsync()
    {
        if (_currentTransaction is not null)
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }
}
