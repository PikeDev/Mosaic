using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mosaic.Outbox.EFCore;

/// <summary>
/// Polls <typeparamref name="TDbContext"/>'s <see cref="MosaicScheduledEntry"/> rows and dispatches
/// any whose <see cref="MosaicScheduledEntry.DueAt"/> has passed via the in-process
/// <see cref="IInboundEventDispatcher"/>. Marks rows dispatched on success; failures leave them
/// for the next cycle. Auto-creates the <c>mosaic_scheduled</c> table on first poll.
/// </summary>
public sealed class EFCoreScheduledRelayHostedService<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private static int _tableEnsured;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EFCoreSchedulingOptions _options;
    private readonly ILogger<EFCoreScheduledRelayHostedService<TDbContext>> _logger;

    public EFCoreScheduledRelayHostedService(
        IServiceScopeFactory scopeFactory,
        EFCoreSchedulingOptions options,
        ILogger<EFCoreScheduledRelayHostedService<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
    {
        _logger.LogInformation("EFCoreScheduledRelayHostedService<{Context}> started; polling every {Interval}.",
            typeof(TDbContext).Name, _options.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (System.Exception ex) when (ex is not System.OperationCanceledException)
            {
                _logger.LogError(ex, "EFCoreScheduledRelayHostedService<{Context}>: poll cycle failed; will retry.", typeof(TDbContext).Name);
            }

            try { await System.Threading.Tasks.Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (System.OperationCanceledException) { /* shutdown */ }
        }
    }

    private async System.Threading.Tasks.Task DispatchDueAsync(System.Threading.CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IInboundEventDispatcher>();

        if (System.Threading.Interlocked.Exchange(ref _tableEnsured, 1) == 0)
        {
            await EnsureTableAsync(db, cancellationToken).ConfigureAwait(false);
        }

        var now = System.DateTime.UtcNow;
        var due = await db.Set<MosaicScheduledEntry>()
            .Where(e => e.DispatchedAt == null && e.DueAt <= now)
            .OrderBy(e => e.DueAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (due.Count == 0) return;

        foreach (var entry in due)
        {
            try
            {
                // Use the scheduled row's id as the envelope MessageId so the inbox can dedup if
                // the relay's "dispatched" stamp ever fails to persist and a second tick re-fires.
                // CorrelationId/CausationId on the row preserve the chain identity from when the
                // schedule was first armed — handlers see the same correlation graph as before.
                var headers = new MessageHeaders(
                    entry.Id,
                    string.IsNullOrEmpty(entry.CorrelationId) ? entry.Id.ToString("N") : entry.CorrelationId,
                    entry.CausationId,
                    entry.QueuedAt);
                await dispatcher.DispatchInboundAsync(
                    entry.TypeFullName,
                    headers,
                    new System.Buffers.ReadOnlySequence<byte>(entry.Payload),
                    cancellationToken).ConfigureAwait(false);
                entry.DispatchedAt = System.DateTime.UtcNow;
            }
            catch (System.Exception ex) when (ex is not System.OperationCanceledException)
            {
                _logger.LogError(ex, "EFCoreScheduledRelayHostedService<{Context}>: dispatch of {Type} (id {Id}, key {Key}) failed; will retry.",
                    typeof(TDbContext).Name, entry.TypeFullName, entry.Id, entry.DedupKey);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("EFCoreScheduledRelayHostedService<{Context}>: dispatched {Count} scheduled message(s).",
            typeof(TDbContext).Name, due.Count(e => e.DispatchedAt is not null));
    }

    private static async System.Threading.Tasks.Task EnsureTableAsync(TDbContext db, System.Threading.CancellationToken cancellationToken)
    {
        var entityType = db.Model.FindEntityType(typeof(MosaicScheduledEntry))
            ?? throw new System.InvalidOperationException(
                $"MosaicScheduledEntry isn't in {typeof(TDbContext).Name}'s model. " +
                $"Did you call .UseMosaicOutbox<{typeof(TDbContext).Name}>() on the DbContextOptionsBuilder?");
        var schema = entityType.GetSchema();
        var table = entityType.GetTableName()!;
        var qualified = string.IsNullOrEmpty(schema) ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";

        var sql = $@"
            CREATE TABLE IF NOT EXISTS {qualified} (
                ""Id"" UUID PRIMARY KEY,
                ""DedupKey"" VARCHAR(256) NOT NULL,
                ""DueAt"" TIMESTAMPTZ NOT NULL,
                ""TypeFullName"" VARCHAR(512) NOT NULL,
                ""Payload"" BYTEA NOT NULL,
                ""QueuedAt"" TIMESTAMPTZ NOT NULL,
                ""DispatchedAt"" TIMESTAMPTZ NULL,
                ""CorrelationId"" VARCHAR(64) NOT NULL DEFAULT '',
                ""CausationId"" VARCHAR(64) NULL
            );
            ALTER TABLE {qualified} ADD COLUMN IF NOT EXISTS ""CorrelationId"" VARCHAR(64) NOT NULL DEFAULT '';
            ALTER TABLE {qualified} ADD COLUMN IF NOT EXISTS ""CausationId"" VARCHAR(64) NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_mosaic_scheduled_pending_key
                ON {qualified} (""DedupKey"") WHERE ""DispatchedAt"" IS NULL;
            CREATE INDEX IF NOT EXISTS ix_mosaic_scheduled_due
                ON {qualified} (""DueAt"") WHERE ""DispatchedAt"" IS NULL;";
#pragma warning disable EF1002 // Risk of SQL injection — table name comes from the EF model, not user input.
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
    }
}

public sealed class EFCoreSchedulingOptions
{
    public System.TimeSpan PollInterval { get; set; } = System.TimeSpan.FromMilliseconds(500);
    public int BatchSize { get; set; } = 100;
}
