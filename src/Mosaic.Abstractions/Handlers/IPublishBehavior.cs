namespace Mosaic;

/// <summary>
/// Cross-cutting middleware wrapping the dispatch of an <see cref="IEvent"/>. Behaviors run
/// once per <see cref="ICompositionEngine.Publish"/> call (around the entire handler fan-out),
/// not once per individual handler — so logging, validation, telemetry around the published
/// event are uniform regardless of how many handlers fire.
/// <para>
/// Ordering is set once at the assembly level via
/// <see cref="CompositionConfigurationAttribute.PublishBehaviors"/>; each entry must be an
/// open-generic implementation of this interface (e.g. <c>typeof(LoggingPublishBehavior&lt;&gt;)</c>),
/// which the source generator closes over each concrete event type at compile time.
/// </para>
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public interface IPublishBehavior<in TEvent> where TEvent : IEvent
{
    /// <summary>Wraps the next behavior (or the terminal fan-out) in the chain.</summary>
    ValueTask Handle(
        TEvent notification,
        PublishHandlerDelegate nextHandler,
        ICompositionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Delegate invoked by an <see cref="IPublishBehavior{TEvent}"/> to call the next behavior in
/// the chain — or the terminal in-process fan-out + cross-process transport publish.
/// </summary>
public delegate ValueTask PublishHandlerDelegate();
