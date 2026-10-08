using System.Data;
using System.Data.Common;
using Core.Database;
using CSharpFunctionalExtensions;
using EducationContentService.Core.Database;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Infrastructure;

/// <summary>
///     Simple EF Core transaction manager for integration tests (no Wolverine outbox dependency).
/// </summary>
internal sealed class TestTransactionManager : ITransactionManager, IDisposable, IAsyncDisposable
{
    private readonly EducationDbContext _dbContext;
    private IDbContextTransaction? _currentTransaction;

    public TestTransactionManager(EducationDbContext dbContext) => _dbContext = dbContext;

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
            try { await _currentTransaction.RollbackAsync(cancellationToken); }
            catch { /* rollback after commit failure — best-effort */ }
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

/// <summary>
///     Collects published integration events in-memory for L2-стиль assertions
///     (Pattern A, без durable outbox). Регистрируется как singleton в test factory;
///     <see cref="IntegrationTestsWebFactory.ResetDatabaseAsync"/> чистит коллекцию между тестами.
/// </summary>
public sealed class TestOutboxService : IOutboxService
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<object> _messages = new();

    public Task PublishAsync<T>(T message) where T : class
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task FlushAsync() => Task.CompletedTask;

    /// <summary>Snapshot всех опубликованных сообщений в порядке публикации.</summary>
    public IReadOnlyCollection<object> Messages => _messages.ToArray();

    /// <summary>Фильтр по типу для assert'ов: <c>OfType&lt;MaterialAccessChanged&gt;().Single()</c>.</summary>
    public IEnumerable<T> OfType<T>() where T : class => _messages.OfType<T>();

    public void Clear()
    {
        while (_messages.TryDequeue(out _))
        {
            // drain
        }
    }
}
