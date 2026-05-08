using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Mosaic.Outbox.EFCore;

/// <summary>
/// EF Core <see cref="IScheduledMessageStore"/> implementation. <see cref="ScheduleAsync"/> tracks
/// a new <see cref="MosaicScheduledEntry"/> on the consumer's DbContext, so the next
/// <c>SaveChangesAsync</c> commits it in the same transaction as state changes — atomic with the
/// saga's own progression.
/// <para>
/// <see cref="CancelAsync"/> deletes the still-pending row by dedup key (also part of the next
/// SaveChanges), so cancelling a timeout is naturally atomic with whatever saga state mutation
/// triggered the cancellation (e.g. order moved to Cancelled → cancel buyers-remorse timer).
/// </para>
/// <para>
/// Re-scheduling the same dedup key is idempotent: if a pending row already exists (in the change
/// tracker or persisted), <see cref="ScheduleAsync"/> updates the row's <c>DueAt</c>+<c>Payload</c>
/// in place rather than inserting a duplicate. This makes <c>Saga&lt;T&gt;.Schedule</c> safe to
/// call across re-arming (debouncer) flows without the unique-index conflict.
/// </para>
/// </summary>
public sealed class EFCoreScheduledMessageStore<TDbContext> : IScheduledMessageStore
    where TDbContext : DbContext
{
    private readonly TDbContext _db;

    public EFCoreScheduledMessageStore(TDbContext db)
    {
        _db = db;
    }

    public async System.Threading.Tasks.ValueTask ScheduleAsync(
        string typeFullName,
        MessageHeaders headers,
        System.Buffers.ReadOnlySequence<byte> payload,
        System.DateTime dueAtUtc,
        string dedupKey,
        System.Threading.CancellationToken cancellationToken)
    {
        // Materialise once — the byte[] copy lives on the entity row across SaveChanges and
        // also feeds the change-tracker upsert paths below.
        var bytes = System.Buffers.BuffersExtensions.ToArray(payload);

        // 1. Already-tracked Add for this dedup key (re-arm within the same transaction).
        EntityEntry<MosaicScheduledEntry>? localAdd = null;
        foreach (var entry in _db.ChangeTracker.Entries<MosaicScheduledEntry>())
        {
            if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Added
                && string.Equals(entry.Entity.DedupKey, dedupKey, System.StringComparison.Ordinal))
            {
                localAdd = entry;
                break;
            }
        }
        if (localAdd is not null)
        {
            localAdd.Entity.DueAt = dueAtUtc;
            localAdd.Entity.Payload = bytes;
            localAdd.Entity.TypeFullName = typeFullName;
            // Re-arm preserves the original CorrelationId — the chain identity stays intact
            // across debouncer ticks; only the payload + due-at refresh.
            return;
        }

        // 2. Already-persisted pending row for this dedup key (re-arm across transactions).
        var existing = await _db.Set<MosaicScheduledEntry>()
            .FirstOrDefaultAsync(e => e.DedupKey == dedupKey && e.DispatchedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            existing.DueAt = dueAtUtc;
            existing.Payload = bytes;
            existing.TypeFullName = typeFullName;
            return;
        }

        // 3. Fresh schedule — track a new row.
        _db.Add(new MosaicScheduledEntry
        {
            Id = headers.MessageId,
            DedupKey = dedupKey,
            DueAt = dueAtUtc,
            TypeFullName = typeFullName,
            Payload = bytes,
            QueuedAt = System.DateTime.UtcNow,
            DispatchedAt = null,
            CorrelationId = headers.CorrelationId,
            CausationId = headers.CausationId,
        });
    }

    public async System.Threading.Tasks.ValueTask<bool> CancelAsync(
        string dedupKey,
        System.Threading.CancellationToken cancellationToken)
    {
        var pending = await _db.Set<MosaicScheduledEntry>()
            .FirstOrDefaultAsync(e => e.DedupKey == dedupKey && e.DispatchedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (pending is null) return false;
        _db.Remove(pending);
        return true;
    }

    public async System.Threading.Tasks.ValueTask<int> CancelByPrefixAsync(
        string dedupKeyPrefix,
        System.Threading.CancellationToken cancellationToken)
    {
        var pending = await _db.Set<MosaicScheduledEntry>()
            .Where(e => e.DispatchedAt == null && e.DedupKey.StartsWith(dedupKeyPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pending.Count == 0) return 0;
        _db.RemoveRange(pending);
        return pending.Count;
    }
}
