using System.Data;
using System.Data.Common;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SharedKernel;
using SharedKernel.DomainEvents;

namespace Wolverine.Testing;

/// <summary>
///     Generic <see cref="ITransactionManager"/> для integration-тестов: использует обычный
///     EF Core <c>DbContext.SaveChangesAsync</c> вместо Wolverine durable outbox. Не требует
///     envelope-таблиц в БД — рассчитан на пару с <see cref="DisableAllWolverineMessagePersistence"/>
///     в test factory.
/// </summary>
/// <remarks>
///     Domain events диспетчатся через <see cref="IDomainEventDispatcher"/>, если он зарегистрирован
///     в DI. Без него (ECS / MaterialProcessingService — нет domain events в use case'ах) handler'ы
///     просто сохраняют DbContext.
/// </remarks>
/// <typeparam name="TDbContext">Конкретный DbContext сервиса.</typeparam>
public sealed class TestTransactionManager<TDbContext> : ITransactionManager, IDisposable
    where TDbContext : DbContext
{
    private const int MAX_DISPATCH_ROUNDS = 10;

    private readonly TDbContext _dbContext;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;
    private readonly IReadOnlyDictionary<string, Func<Error>>? _uniqueConstraintMap;
    private IDbContextTransaction? _currentTransaction;

    /// <param name="dbContext">EF Core DbContext for this service.</param>
    /// <param name="domainEventDispatcher">Optional — needed for services with domain events.</param>
    /// <param name="uniqueConstraintMap">
    ///     Optional mapping from DB constraint name → domain <see cref="Error"/> factory.
    ///     When provided, unique-violation exceptions (SqlState 23505) are mapped to the
    ///     corresponding domain error instead of the generic <c>unique.constraint.violation</c>.
    ///     Pass the same map used by the production <c>TransactionManager</c> to keep test
    ///     behaviour identical to prod.
    /// </param>
    public TestTransactionManager(
        TDbContext dbContext,
        IDomainEventDispatcher? domainEventDispatcher = null,
        IReadOnlyDictionary<string, Func<Error>>? uniqueConstraintMap = null)
    {
        _dbContext = dbContext;
        _domainEventDispatcher = domainEventDispatcher;
        _uniqueConstraintMap = uniqueConstraintMap;
    }

    public async Task<UnitResult<Error>> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _currentTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            return GeneralErrors.DatabaseError();
        }

        UnitResult<Error> dispatchResult = await DispatchDomainEventsAsync(cancellationToken);
        if (dispatchResult.IsFailure)
        {
            return dispatchResult;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
        {
            await TryRollbackAsync(cancellationToken);
            return MapUniqueViolation(pg.ConstraintName);
        }
        catch
        {
            await TryRollbackAsync(cancellationToken);
            return GeneralErrors.DatabaseError();
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UnitResult<Error> dispatchResult = await DispatchDomainEventsAsync(cancellationToken);
        if (dispatchResult.IsFailure)
        {
            return dispatchResult;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
        {
            return MapUniqueViolation(pg.ConstraintName);
        }
        catch
        {
            return GeneralErrors.DatabaseError();
        }
    }

    private Error MapUniqueViolation(string? constraintName)
    {
        if (constraintName is not null &&
            _uniqueConstraintMap is not null &&
            _uniqueConstraintMap.TryGetValue(constraintName, out Func<Error>? factory))
        {
            return factory();
        }

        // Mirror production TransactionManager — surface constraint name via InvalidField so
        // handlers can match on a specific constraint (e.g. AccessService.IssueAutoFreeGrants
        // ловит race по uq_plan_grants_user_plan_active).
        return Error.Conflict(
            "unique.constraint.violation",
            "Нарушение уникальности",
            invalidField: constraintName ?? string.Empty);
    }

    private async Task TryRollbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_currentTransaction is not null)
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
        }
        catch
        {
            // best-effort
        }
        finally
        {
            // Match production transaction managers: a rolled-back EF unit of work must not
            // leak failed tracked entries into a later transaction in the same scope.
            _dbContext.ChangeTracker.Clear();
        }
    }

    public DbConnection GetDbConnection()
    {
        DbConnection connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        return connection;
    }

    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _currentTransaction = null;
    }

    public async ValueTask DisposeAsync() => await DisposeTransactionAsync();

    private async Task<UnitResult<Error>> DispatchDomainEventsAsync(CancellationToken ct)
    {
        if (_domainEventDispatcher is null)
        {
            return UnitResult.Success<Error>();
        }

        for (int round = 0; round < MAX_DISPATCH_ROUNDS; round++)
        {
            List<IDomainEvent> events = _dbContext.ChangeTracker
                .Entries()
                .Where(e => e.Entity is IHasDomainEvents)
                .SelectMany(e =>
                {
                    IHasDomainEvents entity = (IHasDomainEvents)e.Entity;
                    List<IDomainEvent> domainEvents = [.. entity.DomainEvents];
                    entity.ClearDomainEvents();
                    return domainEvents;
                })
                .ToList();

            if (events.Count == 0)
            {
                return UnitResult.Success<Error>();
            }

            foreach (IDomainEvent domainEvent in events)
            {
                UnitResult<Error> result = await _domainEventDispatcher.DispatchAsync(domainEvent, ct);
                if (result.IsFailure)
                {
                    return result;
                }
            }
        }

        // Превысили MAX_DISPATCH_ROUNDS — защита от бесконечной рекурсии domain events.
        return GeneralErrors.DatabaseError();
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
