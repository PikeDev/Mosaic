using System.Buffers;
using Microsoft.Extensions.Logging;
using Mosaic.Runtime;

namespace Mosaic.Outbox.Postgres;

/// <summary>
/// <see cref="IEventTransport"/> that writes published events to a <see cref="OutboxStore"/>
/// instead of shipping them immediately. Pair with <see cref="OutboxRelayHostedService"/> which
/// polls the outbox table and ships rows via the underlying transport.
/// <para>
/// Per-process sender id (<see cref="ProcessSenderId"/>) is shared with the relay so both
/// publisher-write and relay-ship produce the same value in the wire envelope (loopback
/// suppression on receivers depends on this).
/// </para>
/// <para>
/// The outbox write happens after the consumer's <c>SaveChangesAsync</c> commit, so a process
/// crash in that window can lose the event. For atomic publish-with-state semantics use
/// <c>Mosaic.Outbox.EFCore.UseEFCoreOutbox&lt;TDbContext&gt;()</c>, which writes outbox rows in
/// the consumer's own transaction.
/// </para>
/// </summary>
public sealed class OutboxEventTransport : IEventTransport
{
    private readonly OutboxStore _store;
    private readonly string _senderId;
    private readonly ILogger<OutboxEventTransport> _logger;

    public OutboxEventTransport(OutboxStore store, ProcessSenderId sender, ILogger<OutboxEventTransport> logger)
    {
        _store = store;
        _senderId = sender.Value;
        _logger = logger;
    }

    public async ValueTask PublishAsync(string subject, MessageHeaders headers, ReadOnlySequence<byte> payload, CancellationToken cancellationToken = default)
    {
        var bytes = payload.ToArray();
        await _store.EnqueueAsync(headers, _senderId, subject, bytes, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Outbox: enqueued {Type} ({Bytes} bytes).", subject, bytes.Length);
    }
}
