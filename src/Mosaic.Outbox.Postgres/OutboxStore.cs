using Npgsql;
using NpgsqlTypes;

namespace Mosaic.Outbox.Postgres;

/// <summary>
/// Encapsulates SQL access to the <c>mosaic_outbox</c> table. Auto-creates the table on first
/// use; insert/select/mark-sent are vanilla operations against a Postgres data source.
/// <para>
/// The relay reads pending rows with <c>FOR UPDATE SKIP LOCKED</c> so multiple relay processes
/// (e.g. multiple instances of the same publishing service) don't ship the same row twice.
/// </para>
/// </summary>
public sealed class OutboxStore
{
    public const string DefaultTableName = "mosaic_outbox";

    private readonly NpgsqlDataSource _dataSource;
    private readonly string _tableName;
    private int _tableEnsured;

    public OutboxStore(NpgsqlDataSource dataSource, string? tableName = null)
    {
        _dataSource = dataSource;
        _tableName = tableName ?? DefaultTableName;
    }

    public async Task EnqueueAsync(MessageHeaders headers, string sender, string typeFullName, byte[] payload, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO {_tableName} (id, sender, type, payload, queued_at, correlation_id, causation_id)
            VALUES (@id, @sender, @type, @payload, @queued_at, @correlation_id, @causation_id);";
        // Use the publisher-stamped MessageId as the row id so the relay envelope and the inbox
        // dedup key all line up — exactly-once for outbox-shipped events.
        cmd.Parameters.AddWithValue("id", headers.MessageId);
        cmd.Parameters.AddWithValue("sender", sender);
        cmd.Parameters.AddWithValue("type", typeFullName);
        cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea) { Value = payload });
        cmd.Parameters.AddWithValue("queued_at", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("correlation_id", headers.CorrelationId);
        cmd.Parameters.AddWithValue("causation_id", (object?)headers.CausationId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads up to <paramref name="batchSize"/> pending rows under FOR UPDATE SKIP LOCKED so other
    /// relay instances poll a disjoint set. Caller must mark them sent (or fail and let the next
    /// poll pick them up) within the lifetime of the returned transaction's connection.
    /// </summary>
    public async Task<IReadOnlyList<OutboxEntry>> ReadPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT id, sender, type, payload, queued_at, sent_at, correlation_id, causation_id
            FROM {_tableName}
            WHERE sent_at IS NULL
            ORDER BY queued_at
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED;";
        cmd.Parameters.AddWithValue("batch_size", batchSize);

        var results = new List<OutboxEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new OutboxEntry(
                Id: reader.GetGuid(0),
                Sender: reader.GetString(1),
                TypeFullName: reader.GetString(2),
                Payload: (byte[])reader.GetValue(3),
                QueuedAt: reader.GetDateTime(4),
                SentAt: reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                CorrelationId: reader.GetString(6),
                CausationId: reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return results;
    }

    public async Task MarkSentAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {_tableName} SET sent_at = @sent_at WHERE id = @id;";
        cmd.Parameters.AddWithValue("sent_at", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _tableEnsured, 1) == 1) return;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {_tableName} (
                id UUID PRIMARY KEY,
                sender TEXT NOT NULL,
                type TEXT NOT NULL,
                payload BYTEA NOT NULL,
                queued_at TIMESTAMPTZ NOT NULL,
                sent_at TIMESTAMPTZ NULL,
                correlation_id TEXT NOT NULL DEFAULT '',
                causation_id TEXT NULL
            );
            -- Idempotent column adds so a pre-existing table without the correlation columns
            -- (created by an older Mosaic version) is upgraded in place — no migration needed.
            ALTER TABLE {_tableName} ADD COLUMN IF NOT EXISTS correlation_id TEXT NOT NULL DEFAULT '';
            ALTER TABLE {_tableName} ADD COLUMN IF NOT EXISTS causation_id TEXT NULL;
            -- Hot relay query: pending rows by queued_at. Partial index keeps it small even when
            -- the table grows large with sent rows that haven't been pruned yet.
            CREATE INDEX IF NOT EXISTS ix_{_tableName}_pending
                ON {_tableName} (queued_at) WHERE sent_at IS NULL;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
