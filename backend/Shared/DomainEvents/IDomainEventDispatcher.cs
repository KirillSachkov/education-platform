using CSharpFunctionalExtensions;

namespace SharedKernel.DomainEvents;

public interface IDomainEventDispatcher
{
    Task<UnitResult<Error>> DispatchAsync(IDomainEvent domainEvent, CancellationToken ct);

    /// <summary>
    /// Dispatches all events from the source entity and clears them.
    /// For manual invocation from handlers (non-EF scenarios: Redis, Dapper, etc.)
    /// </summary>
    /// <param name="source">Entity that contains domain events to dispatch.</param>
    /// <param name="ct">Cancellation token for the asynchronous operation.</param>
    Task<UnitResult<Error>> DispatchAllAsync(IHasDomainEvents source, CancellationToken ct);
}
