using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mosaic.Runtime;

namespace Mosaic.Outbox.Postgres;

/// <summary>
/// Polls the <c>mosaic_outbox</c> table and ships pending rows via the registered
/// <see cref="IOutboxShipper"/> (typically <c>PostgresOutboxShipper</c> from
/// <c>Mosaic.Transport.Postgres</c>). Each cycle is bounded by
/// <see cref="PostgresOutboxOptions.BatchSize"/> + <see cref="PostgresOutboxOptions.PollInterval"/>.
/// </summary>
public sealed class OutboxRelayHostedService(
    OutboxStore store,
    ProcessSenderId sender,
    IOutboxShipper shipper,
    PostgresOutboxOptions options,
    ILogger<OutboxRelayHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxRelayHostedService started; polling every {Interval} (sender={SenderId}).",
            options.PollInterval, sender.Value);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ShipPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "OutboxRelayHostedService: poll cycle failed; will retry.");
            }

            try { await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    private async Task ShipPendingAsync(CancellationToken stop)
    {
        var pending = await store.ReadPendingAsync(options.BatchSize, stop).ConfigureAwait(false);
        if (pending.Count == 0) return;

        // The outbox row id IS the envelope MessageId — stable across redeliveries, so the
        // receiving consumer's inbox can dedup if the transport delivers the envelope twice.
        // Correlation/causation flow through so receivers see the same chain identity.
        var batch = new OutboxShipment[pending.Count];
        for (int i = 0; i < pending.Count; i++)
        {
            var entry = pending[i];
            var headers = new MessageHeaders(
                entry.Id,
                string.IsNullOrEmpty(entry.CorrelationId) ? entry.Id.ToString("N") : entry.CorrelationId,
                entry.CausationId,
                entry.QueuedAt);
            batch[i] = new OutboxShipment(entry.TypeFullName, entry.Sender, headers, entry.Payload);
        }

        await shipper.ShipBatchAsync(batch, stop).ConfigureAwait(false);

        foreach (var entry in pending)
        {
            await store.MarkSentAsync(entry.Id, stop).ConfigureAwait(false);
        }

        logger.LogDebug("OutboxRelayHostedService: shipped {Count} pending row(s).", pending.Count);
    }
}

public sealed class PostgresOutboxOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    public int BatchSize { get; set; } = 100;
}
