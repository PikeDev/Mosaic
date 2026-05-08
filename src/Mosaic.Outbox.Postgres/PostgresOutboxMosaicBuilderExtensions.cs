using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Mosaic.Runtime;
using Mosaic.Transport.Postgres;
using Npgsql;

namespace Mosaic.Outbox.Postgres;

public static class PostgresOutboxMosaicBuilderExtensions
{
    /// <summary>
    /// Wraps the Postgres transport in an outbox: <see cref="IEventTransport.PublishAsync"/> writes
    /// to the <c>mosaic_outbox</c> table; a relay hosted service polls it and ships rows via
    /// <c>pg_notify</c>. Improves at-least-once delivery — events survive a process crash between
    /// publish and the actual NOTIFY. For full atomicity with the consumer's own DbContext commit,
    /// use <c>Mosaic.Outbox.EFCore.UseEFCoreOutbox&lt;TDbContext&gt;()</c> instead.
    /// <para>
    /// Must be chained AFTER <see cref="PostgresTransportMosaicBuilderExtensions.UsePostgresTransport"/> —
    /// the underlying NpgsqlDataSource that <c>UsePostgresTransport</c> registers is reused here.
    /// </para>
    /// </summary>
    public static IMosaicBuilder UsePostgresOutbox(this IMosaicBuilder builder, System.Action<PostgresOutboxOptions>? configure = null)
    {
        var services = builder.Services;
        if (!services.Any(s => s.ServiceType == typeof(NpgsqlDataSource)))
        {
            throw new System.InvalidOperationException(
                "UsePostgresOutbox requires a Postgres transport. Call .UsePostgresTransport(connectionString) first.");
        }

        var options = new PostgresOutboxOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton<ProcessSenderId>();
        services.AddSingleton(sp => new OutboxStore(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddSingleton<OutboxEventTransport>();

        // Replace IEventTransport: outbox now stands in front of the direct Postgres transport.
        // The direct transport is still registered by its concrete type so its background listener
        // (registered as IHostedService) keeps running on the inbound side.
        services.RemoveAll<IEventTransport>();
        services.AddSingleton<IEventTransport>(sp => sp.GetRequiredService<OutboxEventTransport>());

        services.AddHostedService<OutboxRelayHostedService>();
        return builder;
    }
}
