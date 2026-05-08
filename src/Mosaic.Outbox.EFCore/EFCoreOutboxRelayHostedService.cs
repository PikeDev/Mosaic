using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mosaic.Outbox.EFCore;

/// <summary>
/// Polls <typeparamref name="TDbContext"/>'s <see cref="MosaicOutboxEntry"/> rows and ships
/// pending ones via the registered <see cref="IOutboxShipper"/>. Marks rows as sent on success;
/// failures leave them for the next cycle. Auto-creates the <c>mosaic_outbox</c> table on first
/// poll (CREATE TABLE IF NOT EXISTS) so the consumer doesn't need to generate an EF migration
/// just to opt in.
/// <para>
/// Wire-format coupling lives entirely in the shipper (e.g. <c>PostgresOutboxShipper</c>): this
/// relay is transport-agnostic.
/// </para>
/// </summary>
public sealed class EFCoreOutboxRelayHostedService<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private static int _tableEnsured;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxShipper _shipper;
    private readonly EFCoreOutboxOptions _options;
    private readonly ILogger<EFCoreOutboxRelayHostedService<TDbContext>> _logger;

    public EFCoreOutboxRelayHostedService(
        IServiceScopeFactory scopeFactory,
        IOutboxShipper shipper,
        EFCoreOutboxOptions options,
        ILogger<EFCoreOutboxRelayHostedService<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _shipper = shipper;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EFCoreOutboxRelayHostedService<{Context}> started; polling every {Interval}.",
            typeof(TDbContext).Name, _options.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ShipPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "EFCoreOutboxRelayHostedService<{Context}>: poll cycle failed; will retry.", typeof(TDbContext).Name);
            }

            try { await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    private async Task ShipPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        if (System.Threading.Interlocked.Exchange(ref _tableEnsured, 1) == 0)
        {
            await EnsureTableAsync(db, cancellationToken).ConfigureAwait(false);
        }

        // Set<MosaicOutboxEntry>() works because MosaicOutboxModelCustomizer registered the entity
        // — no DbSet property required on the consumer's DbContext.
        var pending = await db.Set<MosaicOutboxEntry>()
            .Where(e => e.SentAt == null)
            .OrderBy(e => e.QueuedAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pending.Count == 0) return;

        // The outbox row's id IS the envelope MessageId — stable across redeliveries, so the
        // receiving consumer's inbox can dedup if the transport delivers more than once.
        // Correlation/causation flow through so the receiver sees the same chain identity.
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

        await _shipper.ShipBatchAsync(batch, cancellationToken).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        foreach (var entry in pending) entry.SentAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("EFCoreOutboxRelayHostedService<{Context}>: shipped {Count} row(s).", typeof(TDbContext).Name, pending.Count);
    }

    private static async Task EnsureTableAsync(TDbContext db, CancellationToken cancellationToken)
    {
        var entityType = db.Model.FindEntityType(typeof(MosaicOutboxEntry))
            ?? throw new InvalidOperationException(
                $"MosaicOutboxEntry isn't in {typeof(TDbContext).Name}'s model. " +
                $"Did you call .UseMosaicOutbox<{typeof(TDbContext).Name}>(sp) on the DbContextOptionsBuilder?");
        var schema = entityType.GetSchema();
        var table = entityType.GetTableName()!;
        var qualified = string.IsNullOrEmpty(schema) ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";

        // Inbox table lives next to outbox + scheduled — same model customizer, same .UseMosaicOutbox call.
        var inboxEntity = db.Model.FindEntityType(typeof(MosaicInboxEntry));
        var inboxTable = inboxEntity?.GetTableName();
        var inboxQualified = inboxEntity is null ? null
            : (string.IsNullOrEmpty(inboxEntity.GetSchema()) ? $"\"{inboxTable}\"" : $"\"{inboxEntity.GetSchema()}\".\"{inboxTable}\"");

        // Postgres-flavoured DDL — Mosaic.Outbox.EFCore currently assumes Postgres. For other
        // providers, omit this auto-create path and manage the tables via your own migration.
        var sql = $@"
            CREATE TABLE IF NOT EXISTS {qualified} (
                ""Id"" UUID PRIMARY KEY,
                ""Sender"" VARCHAR(64) NOT NULL,
                ""TypeFullName"" VARCHAR(512) NOT NULL,
                ""Payload"" BYTEA NOT NULL,
                ""QueuedAt"" TIMESTAMPTZ NOT NULL,
                ""SentAt"" TIMESTAMPTZ NULL,
                ""CorrelationId"" VARCHAR(64) NOT NULL DEFAULT '',
                ""CausationId"" VARCHAR(64) NULL
            );
            ALTER TABLE {qualified} ADD COLUMN IF NOT EXISTS ""CorrelationId"" VARCHAR(64) NOT NULL DEFAULT '';
            ALTER TABLE {qualified} ADD COLUMN IF NOT EXISTS ""CausationId"" VARCHAR(64) NULL;
            CREATE INDEX IF NOT EXISTS ix_mosaic_outbox_pending
                ON {qualified} (""QueuedAt"") WHERE ""SentAt"" IS NULL;
            " + (inboxQualified is null ? "" : $@"
            CREATE TABLE IF NOT EXISTS {inboxQualified} (
                ""MessageId"" UUID PRIMARY KEY,
                ""ConsumedAt"" TIMESTAMPTZ NOT NULL
            );");
#pragma warning disable EF1002 // Risk of SQL injection — table name comes from the EF model, not user input.
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
    }
}

public sealed class EFCoreOutboxOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    public int BatchSize { get; set; } = 100;
}
