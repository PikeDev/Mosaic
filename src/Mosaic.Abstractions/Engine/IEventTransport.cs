using System.Buffers;

namespace Mosaic;

/// <summary>
/// Cross-process delivery hook for events: the engine calls <see cref="PublishAsync"/> after the
/// in-process handler fan-out completes. Default registration is the no-op
/// <c>InProcessOnlyTransport</c>; transport packages (Postgres NOTIFY, NATS, etc.) replace it.
/// <para>
/// The payload is a <see cref="ReadOnlySequence{Byte}"/> rather than <c>byte[]</c> so the engine
/// can hand over a pooled / multi-segment buffer without an intermediate copy. Transports that
/// need a contiguous span can call <c>payload.ToArray()</c> or
/// <see cref="System.Buffers.BuffersExtensions.ToArray{T}"/>.
/// </para>
/// <para>
/// Headers are forwarded verbatim — transports MUST round-trip them in their wire envelope so
/// receivers can rebuild the correlation graph (<see cref="MessageHeaders"/>).
/// </para>
/// </summary>
public interface IEventTransport
{
    /// <summary>Publish a serialised event payload on the named subject.</summary>
    /// <param name="subject">Stable type identifier (typically the event's full name).</param>
    /// <param name="headers">Correlation / causation / message-id stamped by the engine.</param>
    /// <param name="payload">Serialised body — opaque to the transport.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    System.Threading.Tasks.ValueTask PublishAsync(
        string subject,
        MessageHeaders headers,
        ReadOnlySequence<byte> payload,
        System.Threading.CancellationToken cancellationToken = default);
}
