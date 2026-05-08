namespace Mosaic;

/// <summary>
/// Pluggable seam for inbound idempotency. Backs deduplication of events arriving from a transport
/// that may deliver the same envelope more than once (the at-least-once gap that exists between an
/// outbox relay and a consumer).
/// <para>
/// Implementations are expected to be transactional with the consumer's state changes when
/// possible — <c>Mosaic.Outbox.EFCore</c> tracks the inbox row in the consumer's DbContext so the
/// "this message was consumed" record commits atomically with whatever state the handler chain
/// produced. If the handler chain fails partway, the inbox row rolls back too and a redelivery
/// is free to re-process the message.
/// </para>
/// </summary>
public interface IInboxStore
{
    /// <summary>Has the message with this id already been consumed?</summary>
    System.Threading.Tasks.ValueTask<bool> WasConsumedAsync(
        System.Guid messageId,
        System.Threading.CancellationToken cancellationToken);

    /// <summary>
    /// Stage a "this message was consumed" record. The implementation typically defers commit until
    /// the handler chain calls <c>SaveChanges</c>, so the inbox row rides atomically with state.
    /// </summary>
    System.Threading.Tasks.ValueTask MarkConsumedAsync(
        System.Guid messageId,
        System.Threading.CancellationToken cancellationToken);
}
