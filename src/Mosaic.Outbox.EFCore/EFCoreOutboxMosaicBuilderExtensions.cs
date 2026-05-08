using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Mosaic.Runtime;

namespace Mosaic.Outbox.EFCore;

public static class EFCoreOutboxMosaicBuilderExtensions
{
    /// <summary>
    /// Configures Mosaic to write outgoing events into <typeparamref name="TDbContext"/>'s change
    /// tracker at <see cref="Mosaic.ICompositionContext.Publish"/> time. The next
    /// <c>SaveChangesAsync</c> the consumer calls commits outbox rows in the same transaction as
    /// state changes — atomic outbox.
    /// <para>
    /// Wires up: an <see cref="EFCoreOutboxBuffer{TDbContext}"/> that <see cref="Mosaic.ICompositionContext.Publish"/>
    /// stages events into; and an <see cref="EFCoreOutboxRelayHostedService{TDbContext}"/> that
    /// polls the table and ships rows via the underlying Postgres transport.
    /// </para>
    /// <para>
    /// Consumer side: one extra method call on the DbContext registration:
    /// <code>
    /// services.AddDbContext&lt;MyDbContext&gt;((sp, opts) =&gt; opts.UseNpgsql(connStr).UseMosaicOutbox&lt;MyDbContext&gt;());
    /// </code>
    /// The DbContext does not need to declare a <c>MosaicOutbox</c> DbSet, implement an interface,
    /// or touch <c>OnModelCreating</c> — the entity is added to the model via an EF Core
    /// <c>IModelCustomizer</c>. The <c>mosaic_outbox</c> table is auto-created on first relay
    /// poll (CREATE TABLE IF NOT EXISTS).
    /// </para>
    /// </summary>
    public static IMosaicBuilder UseEFCoreOutbox<TDbContext>(this IMosaicBuilder builder, System.Action<EFCoreOutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        var services = builder.Services;
        if (!services.Any(s => s.ServiceType == typeof(IOutboxShipper)))
        {
            throw new System.InvalidOperationException(
                "UseEFCoreOutbox requires an IOutboxShipper to be registered. Call .UsePostgresTransport(connectionString) "
                + "(or another transport package that provides one) before .UseEFCoreOutbox<TDbContext>().");
        }

        var options = new EFCoreOutboxOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton<ProcessSenderId>();
        services.AddScoped<IOutboxBuffer, EFCoreOutboxBuffer<TDbContext>>();
        // Inbox dedup rides the same DbContext so MarkConsumed commits atomically with handler state.
        services.AddScoped<IInboxStore, EFCoreInboxStore<TDbContext>>();

        // The atomic-outbox path supplants the wire-level transport: outbound publishes go into
        // the entity table along with state, and the relay ships them via NOTIFY later. The
        // routing transport is Scoped so saga hosted services that resolve the engine from their
        // scope and call engine.Publish reach the same scope's outbox buffer (and thus the same
        // DbContext change tracker the saga is about to SaveChanges on).
        services.RemoveAll<IEventTransport>();
        services.AddScoped<IEventTransport, OutboxRoutingTransport>();

        services.AddHostedService<EFCoreOutboxRelayHostedService<TDbContext>>();
        return builder;
    }
}
