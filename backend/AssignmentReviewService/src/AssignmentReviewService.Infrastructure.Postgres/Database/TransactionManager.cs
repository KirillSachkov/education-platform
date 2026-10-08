using System.Data;
using System.Data.Common;
using Core.Database;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

namespace AssignmentReviewService.Infrastructure.Postgres.Database;

/// <summary>
///     TransactionManager с durable outbox / TransactionManager with durable outbox.
///
///     Wolverine integration: <c>IDbContextOutbox.SaveChangesAndFlushMessagesAsync</c>
///     атомарно сохраняет domain-state и envelope-записи outbox'а в одной транзакции.
///     После commit Wolverine отправляет сообщения в RabbitMQ. Domain events диспатчатся
///     до SaveChanges (как и в других сервисах платформы).
/// </summary>
public sealed class TransactionManager : ITransactionManager, IDisposable
{
    private readonly IDbContextOutbox<AssignmentReviewServiceDbContext> _outbox;
    private readonly ILogger<TransactionManager> _logger;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private IDbContextTransaction? _currentTransaction;

    public TransactionManager(
        IDbContextOutbox<AssignmentReviewServiceDbContext> outbox,
        ILogger<TransactionManager> logger,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _outbox = outbox;
        _logger = logger;
        _domainEventDispatcher = domainEventDispatcher;
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
            {
                await _outbox.DbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // Вне явной транзакции — Wolverine сам откроет её для атомарного save+flush.
                await _outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }

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

    private async Task<UnitResult<Error>> DispatchDomainEventsAsync(CancellationToken ct)
    {
        while (true)
        {
            List<IDomainEvent> events = _outbox.DbContext.ChangeTracker
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
