using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Mosaic.Transport.Postgres;

public static class PostgresTransportMosaicBuilderExtensions
{
    /// <summary>
    /// Configures Mosaic's <see cref="IEventTransport"/> to use Postgres LISTEN/NOTIFY for
    /// cross-process event delivery. Replaces the default no-op transport that <c>AddMosaic()</c>
    /// registers via <c>TryAdd</c>, wires the listener as a hosted service, and replaces the
    /// in-memory <see cref="IDeadLetterStore"/> with a Postgres-backed one (table auto-created on
    /// first dead-letter write).
    /// <para>
    /// Usage: <c>services.AddMosaic().UsePostgresTransport(connectionString);</c>
    /// </para>
    /// </summary>
    public static IMosaicBuilder UsePostgresTransport(this IMosaicBuilder builder, string connectionString)
    {
        var services = builder.Services;
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<PostgresEventTransport>();
        services.AddSingleton<IEventTransport>(sp => sp.GetRequiredService<PostgresEventTransport>());
        services.AddHostedService(sp => sp.GetRequiredService<PostgresEventTransport>());
        // Replace the default in-memory DLQ with a Postgres-backed one so dead-lettered envelopes
        // survive process restart and can be inspected from outside the process.
        services.AddSingleton<IDeadLetterStore>(sp => new PostgresDeadLetterStore(sp.GetRequiredService<NpgsqlDataSource>()));
        // Outbox shipper — used by relays (Mosaic.Outbox.EFCore, Mosaic.Outbox.Postgres) to ship
        // rows via NOTIFY without depending on this assembly. Registering it here means
        // .UsePostgresTransport().UseEFCoreOutbox<TDb>() just works.
        services.AddSingleton<IOutboxShipper>(sp => new PostgresOutboxShipper(sp.GetRequiredService<NpgsqlDataSource>()));
        return builder;
    }
}
