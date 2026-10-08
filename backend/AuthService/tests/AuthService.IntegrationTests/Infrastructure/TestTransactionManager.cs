using System.Data;
using System.Data.Common;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using AuthService.Infrastructure.Postgres;
using SharedKernel;
using SharedKernel.DomainEvents;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Test transaction manager that persists only through EF Core DbContext
/// without Wolverine DbContextOutbox dependencies.
/// </summary>
internal sealed class TestTransactionManager : ITransactionManager, IDisposable
{
    private readonly AuthDbContext _dbContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly TestOutboxBuffer _outboxBuffer;
    private readonly Wolverine.Testing.TestOutboxCollector _outboxCollector;
    private IDbContextTransaction? _currentTransaction;

    public TestTransactionManager(
        AuthDbContext dbContext,
        IDomainEventDispatcher domainEventDispatcher,
        TestOutboxBuffer outboxBuffer,
        Wolverine.Testing.TestOutboxCollector outboxCollector)
    {
        _dbContext = dbContext;
        _domainEventDispatcher = domainEventDispatcher;
        _outboxBuffer = outboxBuffer;
        _outboxCollector = outboxCollector;
    }

    public async Task<UnitResult<Error>> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _currentTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
            return GeneralErrors.DatabaseError();

        UnitResult<Error> dispatchResult = await DispatchDomainEventsAsync(cancellationToken);
        if (dispatchResult.IsFailure)
            return dispatchResult;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _currentTransaction.CommitAsync(cancellationToken);
        _outboxBuffer.FlushTo(_outboxCollector);
        await DisposeTransactionAsync();
        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UnitResult<Error> dispatchResult = await DispatchDomainEventsAsync(cancellationToken);
        if (dispatchResult.IsFailure)
            return dispatchResult;

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (_currentTransaction is null)
            _outboxBuffer.FlushTo(_outboxCollector);
        return UnitResult.Success<Error>();
    }

    public DbConnection GetDbConnection()
    {
        DbConnection connection = _dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            connection.Open();

        return connection;
    }

    public async ValueTask DisposeAsync() => await DisposeTransactionAsync();

    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _currentTransaction = null;
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

    private async Task DisposeTransactionAsync()
    {
        if (_currentTransaction is not null)
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }
}
