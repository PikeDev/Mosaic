namespace Mosaic;

/// <summary>
/// Handles a notification (<see cref="IEvent"/>). Many handlers per event type; all run on each publish.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    /// <summary>
    /// Reacts to the event.
    /// </summary>
    /// <param name="notification">The published event.</param>
    /// <param name="context">Composition context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask Handle(
        TEvent notification,
        ICompositionContext context,
        CancellationToken cancellationToken = default);
}
