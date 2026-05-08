using Microsoft.EntityFrameworkCore;

namespace Mosaic.Outbox.EFCore;

/// <summary>
/// EF Core <see cref="IInboxStore"/> implementation. <see cref="WasConsumedAsync"/> queries the
/// <see cref="MosaicInboxEntry"/> table; <see cref="MarkConsumedAsync"/> stages a new row in the
/// consumer's DbContext so the next <c>SaveChangesAsync</c> commits it atomically with handler
/// state changes.
/// </summary>
public sealed class EFCoreInboxStore<TDbContext> : IInboxStore where TDbContext : DbContext
{
    private readonly TDbContext _db;

    public EFCoreInboxStore(TDbContext db)
    {
        _db = db;
    }

    public async System.Threading.Tasks.ValueTask<bool> WasConsumedAsync(System.Guid messageId, System.Threading.CancellationToken cancellationToken)
    {
        // AsNoTracking — we don't need the entity tracked, just the existence check.
        return await _db.Set<MosaicInboxEntry>()
            .AsNoTracking()
            .AnyAsync(e => e.MessageId == messageId, cancellationToken)
            .ConfigureAwait(false);
    }

    public System.Threading.Tasks.ValueTask MarkConsumedAsync(System.Guid messageId, System.Threading.CancellationToken cancellationToken)
    {
        _db.Add(new MosaicInboxEntry { MessageId = messageId, ConsumedAt = System.DateTime.UtcNow });
        return default;
    }
}
