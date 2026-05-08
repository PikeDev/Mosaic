using System.Collections.Concurrent;

namespace Mosaic.Runtime;

/// <summary>
/// In-memory <see cref="IMessageAuditStore"/> intended for tests, samples, and webshop demos.
/// Captures every audit entry in a thread-safe queue; <see cref="Snapshot"/> exposes the rows
/// for assertions and <see cref="ByCorrelation"/> walks one async flow's chain in time order.
/// <para>
/// Not for production — entries are unbounded and process-local. Swap in a durable adapter
/// (EF Core, queue, log pipeline) for real deployments.
/// </para>
/// </summary>
public sealed class InMemoryMessageAuditStore : IMessageAuditStore
{
    private readonly ConcurrentQueue<MessageAuditEntry> _entries = new();

    public System.Threading.Tasks.ValueTask WriteAsync(
        MessageAuditEntry entry,
        System.Threading.CancellationToken cancellationToken = default)
    {
        _entries.Enqueue(entry);
        return default;
    }

    /// <summary>Snapshot the current audit log. Order: insertion order (which is roughly chronological per process).</summary>
    public IReadOnlyList<MessageAuditEntry> Snapshot() => _entries.ToArray();

    /// <summary>
    /// Every audit row sharing <paramref name="correlationId"/>, ordered by <see cref="MessageAuditEntry.TimestampUtc"/>.
    /// Walks one async flow end-to-end — the canonical view for "what did this saga do?".
    /// </summary>
    public IEnumerable<MessageAuditEntry> ByCorrelation(string correlationId)
        => _entries
            .Where(e => string.Equals(e.Headers.CorrelationId, correlationId, System.StringComparison.Ordinal))
            .OrderBy(e => e.TimestampUtc);

    /// <summary>Discard every captured entry — useful between test cases.</summary>
    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { /* drain */ }
    }
}
