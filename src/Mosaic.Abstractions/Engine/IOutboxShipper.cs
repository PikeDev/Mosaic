namespace Mosaic;

/// <summary>
/// One row to be shipped from an outbox table to the configured wire transport. Carries
/// everything the transport needs to construct its on-the-wire envelope: the originating
/// process id (for loopback suppression on receivers), the type identifier, the correlation
/// graph headers, and the serialised payload.
/// </summary>
public sealed record OutboxShipment(
    string TypeFullName,
    string Sender,
    MessageHeaders Headers,
    System.ReadOnlyMemory<byte> Payload);

/// <summary>
/// Wire-level shipper resolved by outbox relay hosted services. Each transport package
/// (e.g. <c>Mosaic.Transport.Postgres</c>) provides its own implementation; the relays stay
/// transport-agnostic and just iterate <see cref="OutboxShipment"/> batches read from their
/// outbox table.
/// <para>
/// Batches are ship-or-fail: <see cref="ShipBatchAsync"/> succeeds only when every shipment
/// has been handed to the wire. A throw mid-batch leaves the relay's mark-sent step un-run, so
/// the next poll re-ships the un-marked rows. Receivers dedup via the inbox keyed on
/// <see cref="MessageHeaders.MessageId"/>.
/// </para>
/// </summary>
public interface IOutboxShipper
{
    System.Threading.Tasks.ValueTask ShipBatchAsync(
        System.Collections.Generic.IReadOnlyList<OutboxShipment> batch,
        System.Threading.CancellationToken cancellationToken);
}
