using System.Buffers;

namespace Mosaic;

/// <summary>
/// Sink for events that a transport's inbound dispatch failed to process after retries were
/// exhausted. Implementations decide where to put them — a Postgres table, a file, an external
/// monitoring system. Default implementation is in-memory (useful for tests and dev); replace via
/// transport-specific configuration.
/// </summary>
public interface IDeadLetterStore
{
    /// <summary>Records a permanently-failed event so an operator can inspect, replay, or discard it.</summary>
    System.Threading.Tasks.Task WriteAsync(
        string typeFullName,
        ReadOnlySequence<byte> payload,
        string errorMessage,
        string errorStack,
        System.Threading.CancellationToken cancellationToken);
}
