using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Mosaic.Outbox.EFCore;

public static class MosaicOutboxDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Wires the Mosaic atomic outbox into a <see cref="DbContext"/> registration:
    /// <list type="bullet">
    /// <item>Adds the <see cref="MosaicOutboxEntry"/> entity to the model via
    /// <see cref="MosaicOutboxModelCustomizer"/> — no DbSet declaration or <c>OnModelCreating</c>
    /// touch required on the consumer side.</item>
    /// <item>Suppresses EF Core's <c>PendingModelChangesWarning</c> for the new entity — the relay
    /// auto-creates the table at startup, so no consumer migration is needed.</item>
    /// </list>
    /// <para>
    /// Usage:
    /// <code>
    /// services.AddDbContext&lt;MyDbContext&gt;((sp, opts) =&gt; opts.UseNpgsql(connStr).UseMosaicOutbox&lt;MyDbContext&gt;());
    /// services.AddMosaic().UsePostgresTransport(connStr).UseEFCoreOutbox&lt;MyDbContext&gt;();
    /// </code>
    /// </para>
    /// </summary>
    public static DbContextOptionsBuilder UseMosaicOutbox<TDbContext>(this DbContextOptionsBuilder optionsBuilder)
        where TDbContext : DbContext
    {
        optionsBuilder.ReplaceService<IModelCustomizer, MosaicOutboxModelCustomizer>();
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        return optionsBuilder;
    }
}
