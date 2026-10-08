using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Infrastructure.Postgres;

namespace MaterialProcessingService.IntegrationTests.Infrastructure;

internal sealed class TestTransactionManager : ITransactionManager, IDisposable, IAsyncDisposable
{
    private readonly MaterialProcessingServiceDbContext _dbContext;
    private IDbContextTransaction? _currentTransaction;

    public TestTransactionManager(MaterialProcessingServiceDbContext dbContext) => _dbContext = dbContext;

    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _currentTransaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentTransaction is not null)
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch
        {
            try
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                // best-effort
            }

            return GeneralErrors.DatabaseError();
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    public async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch
        {
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
}

public sealed class TestOutboxCollector
{
    private readonly ConcurrentQueue<object> _messages = new();

    public IReadOnlyCollection<object> Messages => _messages.ToArray();

    public void Add(object message) => _messages.Enqueue(message);

    public void Clear()
    {
        while (_messages.TryDequeue(out _))
        {
            // drain
        }
    }
}

internal sealed class TestOutboxService(TestOutboxCollector collector) : IOutboxService
{
    public Task PublishAsync<T>(T message) where T : class
    {
        collector.Add(message);
        return Task.CompletedTask;
    }
}
