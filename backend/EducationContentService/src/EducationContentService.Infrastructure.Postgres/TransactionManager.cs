using System.Data;
using System.Data.Common;
using Core.Database;
using EducationContentService.Infrastructure.Postgres.Configurations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

namespace EducationContentService.Infrastructure.Postgres;

public sealed class TransactionManager : ITransactionManager, IDisposable
{
    private static readonly Dictionary<string, string> _constraintToFieldMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { MaterialsIndex.TITLE, "Название материала" },
        { CoursesIndex.TITLE, "Название курса" },
        { IssuesIndex.TITLE, "Название задачи" },
        { ProjectsIndex.TITLE, "Название проекта" },
        { QuizzesIndex.TITLE, "Название квиза" }
    };

    private readonly ILogger<TransactionManager> _logger;
    private readonly IDbContextOutbox<EducationDbContext> _outbox;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private IDbContextTransaction? _currentTransaction;

    public TransactionManager(
        IDbContextOutbox<EducationDbContext> outbox,
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
        {
            return GeneralErrors.DatabaseError();
        }

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

    public async ValueTask DisposeAsync() => await DisposeTransactionAsync();

    public DbConnection GetDbConnection()
    {
        DbConnection connection = _outbox.DbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            connection.Open();

        return connection;
    }

    public async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken)
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
                await _outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }

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

    private Error HandlePostgresException(PostgresException pgEx)
    {
        if (string.Equals(pgEx.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
        {
            string fieldName = GetFieldNameFromConstraint(pgEx.ConstraintName);
            _logger.LogWarning("Unique constraint violation: {Constraint} -> {Field}",
                pgEx.ConstraintName, fieldName);

            return GeneralErrors.UniqueConstraintViolation(fieldName);
        }

        if (string.Equals(pgEx.SqlState, PostgresErrorCodes.ForeignKeyViolation, StringComparison.Ordinal))
        {
            string fieldName = GetFieldNameFromConstraint(pgEx.ConstraintName);
            _logger.LogWarning("Foreign key violation: {Constraint} -> {Field}",
                pgEx.ConstraintName, fieldName);

            return GeneralErrors.ForeignKeyViolation(fieldName);
        }

        _logger.LogError(pgEx, "Database error: {SqlState}", pgEx.SqlState);
        return GeneralErrors.DatabaseError();
    }

    private static string GetFieldNameFromConstraint(string? constraintName)
    {
        if (string.IsNullOrEmpty(constraintName))
        {
            return "Поле";
        }

        if (_constraintToFieldMap.TryGetValue(constraintName, out string? fieldName))
        {
            return fieldName;
        }

        string[] parts = constraintName.ToLowerInvariant().Split('_');
        if (parts.Length >= 3)
        {
            string field = parts[^1];
            return field switch
            {
                "title" => "Название",
                "description" => "Описание",
                "email" => "Email",
                "username" => "Имя пользователя",
                "phone" => "Телефон",
                "slug" => "URL-адрес",
                _ => "Поле"
            };
        }

        return "Поле";
    }

    private async Task RollbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_currentTransaction is not null)
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
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
