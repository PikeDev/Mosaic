using System.Buffers;
using System.Collections.Concurrent;

namespace Mosaic.Runtime;

/// <summary>
/// Default <see cref="IDeadLetterStore"/> registered by <c>AddMosaic()</c>. Holds dead-lettered
/// envelopes in memory; survives only the process lifetime. Useful for tests and dev where the
/// expectation is "no events should be dead-lettered" — assertions can read <see cref="Snapshot"/>
/// after a test to verify that. Replace with a durable store (e.g. Postgres) for production.
/// </summary>
public sealed class InMemoryDeadLetterStore : IDeadLetterStore
{
    private readonly ConcurrentQueue<DeadLetter> _items = new();

    public System.Threading.Tasks.Task WriteAsync(
        string typeFullName,
        ReadOnlySequence<byte> payload,
        string errorMessage,
        string errorStack,
        System.Threading.CancellationToken cancellationToken)
    {
        // Copy out — caller may pool the buffer; in-memory store keeps the bytes for snapshot.
        _items.Enqueue(new DeadLetter(typeFullName, payload.ToArray(), errorMessage, errorStack, System.DateTime.UtcNow));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Snapshot of all dead-lettered envelopes recorded so far. Empty in the happy path.</summary>
    public System.Collections.Generic.IReadOnlyList<DeadLetter> Snapshot() => _items.ToArray();
}

public sealed record DeadLetter(string TypeFullName, byte[] JsonPayload, string ErrorMessage, string ErrorStack, System.DateTime At);
