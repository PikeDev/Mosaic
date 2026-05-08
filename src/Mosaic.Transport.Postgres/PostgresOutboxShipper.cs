using System.Text.Json;
using Npgsql;

namespace Mosaic.Transport.Postgres;

/// <summary>
/// <see cref="IOutboxShipper"/> that ships rows via Postgres <c>NOTIFY</c> on the same channel +
/// envelope shape as <see cref="PostgresEventTransport"/>. Registered automatically by
/// <see cref="PostgresTransportMosaicBuilderExtensions.UsePostgresTransport"/>; outbox relay
/// hosted services (EF Core or standalone Postgres) resolve it via DI and stay transport-agnostic.
/// </summary>
public sealed class PostgresOutboxShipper : IOutboxShipper
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresOutboxShipper(NpgsqlDataSource dataSource)
    {
        System.ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async System.Threading.Tasks.ValueTask ShipBatchAsync(
        System.Collections.Generic.IReadOnlyList<OutboxShipment> batch,
        System.Threading.CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        // One connection for the whole batch — Npgsql's data source pools internally, but
        // re-using the same handle avoids the per-row pool round-trip.
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var shipment in batch)
        {
            var envelope = new PostgresEventTransport.Envelope(
                shipment.Sender,
                shipment.TypeFullName,
                System.Convert.ToBase64String(shipment.Payload.Span),
                shipment.Headers.MessageId,
                CorrelationId: string.IsNullOrEmpty(shipment.Headers.CorrelationId) ? null : shipment.Headers.CorrelationId,
                CausationId: shipment.Headers.CausationId,
                SentAtUtc: shipment.Headers.SentAtUtc);
            var envelopeJson = JsonSerializer.Serialize(envelope);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_notify(@channel, @payload)";
            cmd.Parameters.AddWithValue("channel", PostgresEventTransport.Channel);
            cmd.Parameters.AddWithValue("payload", envelopeJson);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
