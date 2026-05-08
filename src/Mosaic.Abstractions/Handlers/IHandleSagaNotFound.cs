namespace Mosaic;

/// <summary>
/// Opt-in hook for handling messages whose target saga / aggregate doesn't exist (yet, or any
/// more). When an event handler does its lookup and finds nothing, it can route the orphaned
/// message to an <see cref="IHandleSagaNotFound{TMessage}"/> via
/// <c>ICompositionContext.HandleSagaNotFoundAsync(message, cancellationToken)</c>. If no handler
/// is registered for the message type, the call is a debug-logged no-op.
/// <para>
/// Common uses: log to an audit trail, schedule a retry (saga may not have arrived yet), trigger a
/// compensation flow, or wake an operator. ADSD's "saga-not-found" pattern — turns silent orphans
/// into typed, debuggable hooks.
/// </para>
/// </summary>
/// <typeparam name="TMessage">The message type whose orphaned arrivals you want to handle.</typeparam>
public interface IHandleSagaNotFound<TMessage> where TMessage : IEvent
{
    System.Threading.Tasks.ValueTask OnNotFoundAsync(
        TMessage message,
        ICompositionContext context,
        System.Threading.CancellationToken cancellationToken);
}
