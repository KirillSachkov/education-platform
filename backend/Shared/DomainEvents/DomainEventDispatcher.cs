using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;

namespace SharedKernel.DomainEvents;

public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(
        IServiceProvider serviceProvider,
        ILogger<DomainEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> DispatchAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        Type eventType = domainEvent.GetType();
        Type wrapperType = typeof(DomainEventHandlerWrapper<>).MakeGenericType(eventType);

        object? wrapper = _serviceProvider.GetService(wrapperType);
        if (wrapper is null)
        {
            _logger.LogDebug("No handlers registered for domain event {EventType}", eventType.Name);
            return UnitResult.Success<Error>();
        }

        return await ((IDomainEventHandlerWrapper)wrapper).HandleAsync(domainEvent, ct);
    }

    public async Task<UnitResult<Error>> DispatchAllAsync(IHasDomainEvents source, CancellationToken ct)
    {
        List<IDomainEvent> events = [.. source.DomainEvents];
        source.ClearDomainEvents();

        foreach (IDomainEvent domainEvent in events)
        {
            UnitResult<Error> result = await DispatchAsync(domainEvent, ct);
            if (result.IsFailure)
                return result;
        }

        return UnitResult.Success<Error>();
    }
}

internal interface IDomainEventHandlerWrapper
{
    Task<UnitResult<Error>> HandleAsync(IDomainEvent domainEvent, CancellationToken ct);
}

internal sealed class DomainEventHandlerWrapper<TEvent> : IDomainEventHandlerWrapper
    where TEvent : IDomainEvent
{
    private readonly IEnumerable<IDomainEventHandler<TEvent>> _handlers;
    private readonly ILogger<DomainEventHandlerWrapper<TEvent>> _logger;

    public DomainEventHandlerWrapper(
        IEnumerable<IDomainEventHandler<TEvent>> handlers,
        ILogger<DomainEventHandlerWrapper<TEvent>> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> HandleAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        var typedEvent = (TEvent)domainEvent;

        foreach (IDomainEventHandler<TEvent> handler in _handlers)
        {
            _logger.LogDebug(
                "Dispatching {EventType} to {HandlerType}",
                typeof(TEvent).Name,
                handler.GetType().Name);

            UnitResult<Error> result = await handler.Handle(typedEvent, ct);
            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "Handler {HandlerType} failed for {EventType}: {Error}",
                    handler.GetType().Name,
                    typeof(TEvent).Name,
                    result.Error);

                return result;
            }
        }

        return UnitResult.Success<Error>();
    }
}
