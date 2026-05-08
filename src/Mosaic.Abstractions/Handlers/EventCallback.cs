namespace Mosaic;

/// <summary>
/// Delegate signature for an inline event subscription registered via
/// <see cref="ICompositionContext.Subscribe{TEvent}"/>.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public delegate ValueTask EventCallback<in TEvent>(
    TEvent notification,
    ICompositionContext context,
    CancellationToken cancellationToken)
    where TEvent : IEvent;
