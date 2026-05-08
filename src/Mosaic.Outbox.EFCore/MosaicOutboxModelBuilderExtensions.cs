using Microsoft.EntityFrameworkCore;

namespace Mosaic.Outbox.EFCore;

/// <summary>
/// Model-builder extensions for wiring Mosaic's outbox/scheduled/inbox entities into a consumer
/// <see cref="DbContext"/> via <c>OnModelCreating</c>. Use this from <c>OnModelCreating</c>:
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     // ... your service entities ...
///     modelBuilder.ApplyMosaicOutbox();
/// }
/// </code>
/// <para>
/// Works in both runtime and design-time (i.e. <c>dotnet ef migrations add</c>) because the
/// model creation path runs in both contexts. Prefer this over the
/// <c>UseMosaicOutbox&lt;TDbContext&gt;()</c> options-based approach when generating migrations —
/// the latter only fires at runtime, so generated migrations would silently miss the framework
/// tables.
/// </para>
/// </summary>
public static class MosaicOutboxModelBuilderExtensions
{
    public const string OutboxTableName = "mosaic_outbox";
    public const string ScheduledTableName = "mosaic_scheduled";
    public const string InboxTableName = "mosaic_inbox";

    /// <summary>
    /// Adds the outbox, scheduled-message, and inbox entities to the model. Idempotent — if
    /// already configured (e.g. via <c>UseMosaicOutbox</c>), this call is a no-op.
    /// </summary>
    public static ModelBuilder ApplyMosaicOutbox(this ModelBuilder modelBuilder)
    {
        // Idempotent: if the outbox entity is already in the model, all three are.
        if (modelBuilder.Model.FindEntityType(typeof(MosaicOutboxEntry)) is not null) return modelBuilder;

        modelBuilder.Entity<MosaicOutboxEntry>(b =>
        {
            b.ToTable(OutboxTableName);
            b.HasKey(e => e.Id);
            b.Property(e => e.Sender).HasMaxLength(64);
            b.Property(e => e.TypeFullName).HasMaxLength(512);
            b.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            b.Property(e => e.CausationId).HasMaxLength(64);
            b.HasIndex(e => e.QueuedAt).HasFilter("\"SentAt\" IS NULL");
        });

        modelBuilder.Entity<MosaicScheduledEntry>(b =>
        {
            b.ToTable(ScheduledTableName);
            b.HasKey(e => e.Id);
            b.Property(e => e.DedupKey).HasMaxLength(256).IsRequired();
            b.Property(e => e.TypeFullName).HasMaxLength(512);
            b.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            b.Property(e => e.CausationId).HasMaxLength(64);
            // Unique among PENDING rows so re-scheduling the same key is an upsert (handled by
            // EFCoreScheduledMessageStore.ScheduleAsync), and historic dispatched rows don't
            // block future re-schedules with the same key.
            b.HasIndex(e => e.DedupKey).IsUnique().HasFilter("\"DispatchedAt\" IS NULL");
            b.HasIndex(e => e.DueAt).HasFilter("\"DispatchedAt\" IS NULL");
        });

        modelBuilder.Entity<MosaicInboxEntry>(b =>
        {
            b.ToTable(InboxTableName);
            b.HasKey(e => e.MessageId);
        });

        return modelBuilder;
    }
}
