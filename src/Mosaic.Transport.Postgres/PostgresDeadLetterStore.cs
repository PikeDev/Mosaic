using System.Buffers;
using Npgsql;
using NpgsqlTypes;

namespace Mosaic.Transport.Postgres;

/// <summary>
/// <see cref="IDeadLetterStore"/> that writes failed envelopes to a Postgres table. The table is
/// auto-created on first use; rows are append-only with the original payload, error details, and
/// timestamp so an operator can later inspect, replay, or discard them.
/// </summary>
public sealed class PostgresDeadLetterStore : IDeadLetterStore
{
    public const string DefaultTableName = "mosaic_dead_letters";

    private readonly NpgsqlDataSource _dataSource;
    private readonly string _tableName;
    private int _tableEnsured;

    public PostgresDeadLetterStore(NpgsqlDataSource dataSource, string? tableName = null)
    {
        _dataSource = dataSource;
        _tableName = tableName ?? DefaultTableName;
    }

    public async Task WriteAsync(
        string typeFullName,
        ReadOnlySequence<byte> payload,
        string errorMessage,
        string errorStack,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        // Bytea parameter wants a contiguous byte[] — copy the (possibly multi-segment) payload.
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO {_tableName} (id, type, payload, error_message, error_stack, dead_lettered_at)
            VALUES (@id, @type, @payload, @error_message, @error_stack, @dead_lettered_at);";
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("type", typeFullName);
        cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea) { Value = payload.ToArray() });
        cmd.Parameters.AddWithValue("error_message", errorMessage);
        cmd.Parameters.AddWithValue("error_stack", errorStack);
        cmd.Parameters.AddWithValue("dead_lettered_at", DateTime.UtcNow);
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
                type TEXT NOT NULL,
                payload BYTEA NOT NULL,
                error_message TEXT NOT NULL,
                error_stack TEXT NOT NULL,
                dead_lettered_at TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_{_tableName}_type_at ON {_tableName} (type, dead_lettered_at);";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
