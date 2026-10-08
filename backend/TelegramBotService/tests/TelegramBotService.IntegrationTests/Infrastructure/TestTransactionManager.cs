using System.Data.Common;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel;
using TelegramBotService.Infrastructure.Postgres;

namespace TelegramBotService.IntegrationTests.Infrastructure;

/// <summary>
/// Минимальная реализация <see cref="ITransactionManager"/> для handler-level тестов.
/// Без Wolverine outbox'а — просто DbContext.SaveChangesAsync.
///
/// Тесты handler'ов TelegramBotService не trigger'ят cross-service events, так что outbox
/// не нужен. Если в будущем handler начнёт publish'ить события через outbox — заменить.
/// </summary>
internal sealed class TestTransactionManager : ITransactionManager
{
    private readonly TelegramBotDbContext _db;
    private IDbContextTransaction? _transaction;

    public TestTransactionManager(TelegramBotDbContext db) => _db = db;

    public async Task<UnitResult<Error>> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            return GeneralErrors.DatabaseError();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch
        {
            await _transaction.RollbackAsync(cancellationToken);
            return GeneralErrors.DatabaseError();
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    public DbConnection GetDbConnection() => _db.Database.GetDbConnection();

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
