using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Marker interface for ecommerce sample domain events.
/// </summary>
public interface IDomainEvent
{
}

/// <summary>
/// Handles an ecommerce sample domain event.
/// </summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the domain event.
    /// </summary>
    /// <param name="domainEvent">The domain event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when handling is done.</returns>
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

/// <summary>
/// Dispatches ecommerce sample domain events in-process.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches a domain event to registered in-process handlers.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    /// <param name="domainEvent">The domain event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when dispatch is done.</returns>
    Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent;
}

/// <summary>
/// Default in-process domain event dispatcher for the ecommerce sample.
/// </summary>
public sealed class InProcessDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _services;
    private readonly ILogger<InProcessDomainEventDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessDomainEventDispatcher"/> class.
    /// </summary>
    /// <param name="services">The scoped service provider.</param>
    /// <param name="logger">The dispatcher logger.</param>
    public InProcessDomainEventDispatcher(
        IServiceProvider services,
        ILogger<InProcessDomainEventDispatcher> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var handlers = _services.GetServices<IDomainEventHandler<TEvent>>().ToArray();
        _logger.LogInformation(
            "Dispatching domain event {DomainEventType} to {HandlerCount} handlers.",
            typeof(TEvent).Name,
            handlers.Length);

        foreach (var handler in handlers)
        {
            await handler.HandleAsync(domainEvent, ct);
        }
    }
}
