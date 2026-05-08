using System.Buffers;

namespace Mosaic;

/// <summary>
/// Hooks an inbound transport delivery (Postgres NOTIFY listener, scheduled-message relay, etc.)
/// back into the engine's per-event dispatch. Resolved as a scoped service alongside
/// <see cref="ICompositionEngine"/> — the same generated implementation provides both.
/// </summary>
public interface IInboundEventDispatcher
{
    /// <summary>
    /// Dispatch an inbound payload to its registered <see cref="IEventHandler{TEvent}"/>s.
    /// Returns silently when no handler is registered for <paramref name="typeFullName"/> —
    /// transports survive type drift between deployments without crashing.
    /// </summary>
    /// <param name="typeFullName">CLR full-name of the event the payload deserialises to.</param>
    /// <param name="headers">Correlation graph ids stamped by the publisher; flow into the
    /// per-handler <see cref="ICompositionContext"/> so cascaded publishes inherit them.</param>
    /// <param name="payload">Serialised body — multi-segment-friendly (zero-copy from pipes).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    System.Threading.Tasks.Task DispatchInboundAsync(
        string typeFullName,
        MessageHeaders headers,
        ReadOnlySequence<byte> payload,
        System.Threading.CancellationToken cancellationToken = default);
}
