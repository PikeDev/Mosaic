using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Mosaic.Outbox.EFCore;

/// <summary>
/// EF Core <see cref="IModelCustomizer"/> that calls <see cref="MosaicOutboxModelBuilderExtensions.ApplyMosaicOutbox"/>
/// — the registration path used by <see cref="MosaicOutboxDbContextOptionsBuilderExtensions.UseMosaicOutbox"/>.
/// Works at runtime, but design-time tooling (<c>dotnet ef</c>) bypasses
/// <c>DbContextOptionsBuilder</c> registration via <see cref="IDesignTimeDbContextFactory{TDbContext}"/>,
/// so prefer calling <c>modelBuilder.ApplyMosaicOutbox()</c> directly from your DbContext's
/// <c>OnModelCreating</c> when you need migration support.
/// </summary>
public sealed class MosaicOutboxModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Outbox table name.</summary>
    public const string OutboxTableName = MosaicOutboxModelBuilderExtensions.OutboxTableName;
    /// <summary>Scheduled-message table name.</summary>
    public const string ScheduledTableName = MosaicOutboxModelBuilderExtensions.ScheduledTableName;
    /// <summary>Inbox table name.</summary>
    public const string InboxTableName = MosaicOutboxModelBuilderExtensions.InboxTableName;

    public MosaicOutboxModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.ApplyMosaicOutbox();
    }
}
