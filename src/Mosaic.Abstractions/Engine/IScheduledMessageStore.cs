using System.Buffers;

namespace Mosaic;

/// <summary>
/// Pluggable seam for persisting messages that should be dispatched at a future time. Backs
/// <see cref="ICompositionContext.ScheduleMessage{TMessage}"/> — a saga schedules its own
/// timeout, the store persists it, and a relay dispatches when due.
/// <para>
/// Implementations should be transactional with the consumer's state changes when possible
/// (e.g. <c>Mosaic.Outbox.EFCore</c> stages the row in the DbContext change tracker so the next
/// <c>SaveChangesAsync</c> commits state + schedule together — atomic with the saga's own
/// progression).
/// </para>
/// </summary>
public interface IScheduledMessageStore
{
    /// <summary>
    /// Persist a scheduled message. <paramref name="dedupKey"/> is a caller-supplied string used
    /// to identify the schedule for later <see cref="CancelAsync"/>; it should be unique per
    /// pending schedule. Re-scheduling with the same key is the implementation's choice
    /// (typically idempotent — last-write-wins or upsert).
    /// <paramref name="headers"/> are stored on the row and replayed when the relay dispatches
    /// so the timeout's handler sees the original chain's <see cref="MessageHeaders.CorrelationId"/>.
    /// </summary>
    System.Threading.Tasks.ValueTask ScheduleAsync(
        string typeFullName,
        MessageHeaders headers,
        ReadOnlySequence<byte> payload,
        System.DateTime dueAtUtc,
        string dedupKey,
        System.Threading.CancellationToken cancellationToken);

    /// <summary>
    /// Cancel a still-pending schedule by its <paramref name="dedupKey"/>. Returns <c>true</c>
    /// if a row was found and removed; <c>false</c> if it had already fired or never existed.
    /// </summary>
    System.Threading.Tasks.ValueTask<bool> CancelAsync(
        string dedupKey,
        System.Threading.CancellationToken cancellationToken);

    /// <summary>
    /// Cancel every still-pending schedule whose <c>DedupKey</c> starts with <paramref name="dedupKeyPrefix"/>.
    /// Returns the number of rows removed. Used by the saga lifecycle to clean up timeouts
    /// scheduled with the <c>saga:&lt;id&gt;:</c> prefix when a saga calls <c>Complete</c>, so a
    /// cancelled-or-resolved saga doesn't leave its scheduled timeouts firing into the void.
    /// </summary>
    System.Threading.Tasks.ValueTask<int> CancelByPrefixAsync(
        string dedupKeyPrefix,
        System.Threading.CancellationToken cancellationToken);
}
