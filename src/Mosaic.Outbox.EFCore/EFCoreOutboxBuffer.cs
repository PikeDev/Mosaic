using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mosaic.Runtime;

namespace Mosaic.Outbox.EFCore;

/// <summary>
/// EF Core <see cref="IOutboxBuffer"/> implementation. <see cref="Enqueue"/> tracks a new
/// <see cref="MosaicOutboxEntry"/> on the consumer's DbContext, so the next <c>SaveChangesAsync</c>
/// commits it in the same transaction as state changes — atomic outbox.
/// <para>
/// Handler ordering doesn't matter: <c>Save → Publish → return</c>, <c>Publish → Save → return</c>,
/// and even multiple <c>Save</c>/<c>Publish</c> interleavings all work. If the handler ends with
/// pending outbox rows that no <c>Save</c> flushed (the Save-then-Publish case), <see cref="DisposeAsync"/>
/// performs a fallback save in its own transaction so the event is never silently dropped — a
/// warning is logged because that path isn't atomic with the original state commit.
/// </para>
/// <para>
/// The fallback skips when the change tracker has *other* dirty entities, on the assumption that
/// the handler threw mid-work; in that case both state and outbox stay rolled back, which is the
/// consistent outcome.
/// </para>
/// </summary>
public sealed class EFCoreOutboxBuffer<TDbContext> : IOutboxBuffer, System.IAsyncDisposable, System.IDisposable
    where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly ProcessSenderId _sender;
    private readonly ILogger<EFCoreOutboxBuffer<TDbContext>> _logger;
    private bool _disposed;

    public EFCoreOutboxBuffer(TDbContext db, ProcessSenderId sender, ILogger<EFCoreOutboxBuffer<TDbContext>> logger)
    {
        _db = db;
        _sender = sender;
        _logger = logger;
    }

    public void Enqueue(string typeFullName, MessageHeaders headers, System.Buffers.ReadOnlySequence<byte> payload)
    {
        // The row needs a contiguous byte[] for EF mapping; copy from the (possibly multi-segment)
        // payload now since the buffer may be pooled/recycled the moment the caller returns. Use
        // the publisher-stamped MessageId as the row id so the relay envelope and the inbox dedup
        // key all line up — exactly-once for outbox-shipped events.
        _db.Add(new MosaicOutboxEntry
        {
            Id = headers.MessageId,
            Sender = _sender.Value,
            TypeFullName = typeFullName,
            Payload = System.Buffers.BuffersExtensions.ToArray(payload),
            QueuedAt = System.DateTime.UtcNow,
            SentAt = null,
            CorrelationId = headers.CorrelationId,
            CausationId = headers.CausationId,
        });
    }

    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Don't touch the context if it's already gone (defensive — service-provider should dispose
        // us first because we resolved the DbContext via constructor injection, but be safe).
        try { _ = _db.ChangeTracker; } catch (System.ObjectDisposedException) { return; }

        var pendingMosaic = 0;
        var hasOtherDirty = false;
        foreach (var e in _db.ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (e.Entity is MosaicOutboxEntry or MosaicInboxEntry or MosaicScheduledEntry)
            {
                if (e.State == EntityState.Added) pendingMosaic++;
            }
            else hasOtherDirty = true;
        }

        if (pendingMosaic == 0) return;

        if (hasOtherDirty)
        {
            // The handler likely threw before its final SaveChanges — leave the Mosaic rows un-flushed
            // alongside the state changes so EF rolls everything back together (consistent outcome).
            _logger.LogError(
                "Mosaic outbox/inbox: scope ending with {Count} pending Mosaic row(s) AND unsaved non-Mosaic changes — dropped to keep state consistency.",
                pendingMosaic);
            return;
        }

        try
        {
            await _db.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogDebug(
                "Mosaic outbox/inbox: fallback-saved {Count} pending Mosaic row(s) at scope end.",
                pendingMosaic);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Mosaic outbox/inbox: fallback save failed; {Count} row(s) lost.", pendingMosaic);
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
