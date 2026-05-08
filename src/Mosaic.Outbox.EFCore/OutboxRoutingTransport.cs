using System.Buffers;

namespace Mosaic.Outbox.EFCore;

/// <summary>
/// <see cref="IEventTransport"/> that routes outbound publishes through the scope's
/// <see cref="IOutboxBuffer"/> instead of a wire-level transport. The buffer stages the row
/// in the consumer's DbContext change tracker; the relay polls and ships rows via the underlying
/// transport later.
/// <para>
/// Registered as <em>Scoped</em> so the engine — which is itself Scoped — resolves a transport
/// that already knows its scope's outbox buffer. This is what makes <c>engine.Publish</c> from a
/// saga hosted service write into the outbox: the saga acquired the engine from its scope, and
/// the engine's transport is from the same scope.
/// </para>
/// </summary>
public sealed class OutboxRoutingTransport : IEventTransport
{
    private readonly IOutboxBuffer _buffer;

    public OutboxRoutingTransport(IOutboxBuffer buffer)
    {
        _buffer = buffer;
    }

    public System.Threading.Tasks.ValueTask PublishAsync(
        string subject,
        MessageHeaders headers,
        ReadOnlySequence<byte> payload,
        System.Threading.CancellationToken cancellationToken = default)
    {
        _buffer.Enqueue(subject, headers, payload);
        return default;
    }
}
