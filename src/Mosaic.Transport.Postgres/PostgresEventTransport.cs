using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mosaic.Runtime;
using Npgsql;

namespace Mosaic.Transport.Postgres;

/// <summary>
/// <see cref="IEventTransport"/> implementation backed by Postgres LISTEN/NOTIFY on a single
/// channel (<c>mosaic_events</c>). Each published event is wrapped in a JSON envelope with the
/// sender process id, the event's full type name, and the base64-encoded JSON payload.
/// <para>
/// Inbound dispatch goes through <see cref="InboundDispatch.TryDispatchAsync"/>: handler exceptions
/// trigger retries with backoff; events that exhaust retries land in the configured
/// <see cref="IDeadLetterStore"/> (Postgres-backed by default — see <see cref="PostgresDeadLetterStore"/>).
/// </para>
/// <para>
/// Constraints to be aware of:
/// <list type="bullet">
/// <item>Postgres NOTIFY payloads are capped at ~8000 bytes; events larger than that won't fit.</item>
/// <item>Not durable on its own — notifications are dropped if no listener is connected when they fire.
/// Pair with an outbox (<c>Mosaic.Outbox.Postgres</c> / <c>Mosaic.Outbox.EFCore</c>) for at-least-once
/// <em>publishing</em>; the DLQ already handles inbound failures.</item>
/// <item>Loopback suppression by per-process sender id — a publisher does not receive its own events back.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PostgresEventTransport : IEventTransport, IHostedService, IAsyncDisposable
{
    public const string Channel = "mosaic_events";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeadLetterStore _deadLetterStore;
    private readonly IRecoverabilityPolicy _recoverability;
    private readonly ICriticalErrorHandler _criticalError;
    private readonly ILogger<PostgresEventTransport> _logger;
    private readonly string _senderId = Guid.NewGuid().ToString("N");

    private NpgsqlConnection? _listenConnection;
    private CancellationTokenSource? _stopping;
    private Task? _listenLoop;

    public PostgresEventTransport(
        NpgsqlDataSource dataSource,
        IServiceScopeFactory scopeFactory,
        IDeadLetterStore deadLetterStore,
        IRecoverabilityPolicy recoverability,
        ICriticalErrorHandler criticalError,
        ILogger<PostgresEventTransport> logger)
    {
        _dataSource = dataSource;
        _scopeFactory = scopeFactory;
        _deadLetterStore = deadLetterStore;
        _recoverability = recoverability;
        _criticalError = criticalError;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync(string subject, MessageHeaders headers, ReadOnlySequence<byte> payload, CancellationToken cancellationToken = default)
    {
        // Postgres NOTIFY caps payloads at ~8000 bytes; the base64 inflation is unavoidable since
        // the channel only carries text. Headers ride on the JSON envelope so receivers rebuild
        // the correlation graph (CorrelationId / CausationId / MessageId) without round-tripping
        // through any other store.
        var envelope = new Envelope(
            _senderId,
            subject,
            Convert.ToBase64String(payload.ToArray()),
            headers.MessageId,
            headers.CorrelationId,
            headers.CausationId,
            headers.SentAtUtc);
        var envelopeJson = JsonSerializer.Serialize(envelope);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_notify(@channel, @payload)";
        cmd.Parameters.AddWithValue("channel", Channel);
        cmd.Parameters.AddWithValue("payload", envelopeJson);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenConnection = await _dataSource.OpenConnectionAsync(_stopping.Token).ConfigureAwait(false);
        _listenConnection.Notification += OnNotification;
        await using (var cmd = _listenConnection.CreateCommand())
        {
            cmd.CommandText = $"LISTEN {Channel}";
            await cmd.ExecuteNonQueryAsync(_stopping.Token).ConfigureAwait(false);
        }
        _listenLoop = ListenLoopAsync(_stopping.Token);
        _logger.LogInformation("PostgresEventTransport listening on '{Channel}' (sender={SenderId}).", Channel, _senderId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null) return;
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_listenLoop is not null)
        {
            try { await _listenLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        if (_listenConnection is not null) await _listenConnection.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _stopping?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // WaitAsync returns when a NOTIFY arrives; handler is fired synchronously by Npgsql.
                await _listenConnection!.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgresEventTransport: WaitAsync failed; will retry in 1s.");
                try { await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void OnNotification(object? sender, NpgsqlNotificationEventArgs args)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(args.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgresEventTransport: failed to parse envelope.");
            return;
        }
        if (envelope is null) return;
        if (string.Equals(envelope.Sender, _senderId, StringComparison.Ordinal)) return;   // loopback drop

        byte[] payload;
        try { payload = Convert.FromBase64String(envelope.PayloadBase64); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgresEventTransport: invalid payload base64 for {Type}.", envelope.Type);
            return;
        }

        // Fire-and-forget on the threadpool. The handler is invoked synchronously by Npgsql and
        // we don't want to block the listen-connection's IO. The shared resilience helper handles
        // retries and dead-lettering.
        var headers = new MessageHeaders(
            envelope.MessageId,
            envelope.CorrelationId ?? envelope.MessageId.ToString("N"),
            envelope.CausationId,
            envelope.SentAtUtc ?? DateTime.UtcNow);
        _ = Task.Run(() => InboundDispatch.TryDispatchAsync(
            envelope.Type, headers, new ReadOnlySequence<byte>(payload), _scopeFactory, _deadLetterStore, _recoverability, _criticalError, _logger, CancellationToken.None));
    }

    /// <summary>
    /// JSON envelope used on the wire. Public so the outbox relay can reuse the shape.
    /// <see cref="CorrelationId"/>, <see cref="CausationId"/>, and <see cref="SentAtUtc"/> are nullable
    /// so envelopes from peers that omit them still deserialise; the listener fills sane defaults.
    /// </summary>
    public sealed record Envelope(
        string Sender,
        string Type,
        string PayloadBase64,
        System.Guid MessageId,
        string? CorrelationId = null,
        string? CausationId = null,
        System.DateTime? SentAtUtc = null);
}
