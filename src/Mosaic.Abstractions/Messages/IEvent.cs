namespace Mosaic;

/// <summary>
/// Marks a notification message that is fanned out to <b>many</b>
/// <see cref="IEventHandler{TEvent}"/> implementations. Fire-and-forget — handlers
/// produce no return value.
/// </summary>
/// <remarks>
/// By default, event handlers run in parallel. Configurable to sequential per assembly
/// via <see cref="CompositionConfigurationAttribute.EventPublishMode"/>.
/// <para>
/// Events published from inside a composition (via <see cref="ICompositionContext.Publish{TEvent}"/>)
/// are buffered and flushed after the parent composition completes, matching outbox-style semantics.
/// </para>
/// </remarks>
public interface IEvent;
