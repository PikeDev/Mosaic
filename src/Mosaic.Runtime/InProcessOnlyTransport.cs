using System.Buffers;

namespace Mosaic.Runtime;

/// <summary>
/// Default <see cref="IEventTransport"/> registered by <c>AddMosaic()</c>: a no-op outbound.
/// Events stay in-process — handlers in the publishing process run via the engine's normal
/// dispatch; no event leaves the process.
/// <para>
/// Replace via a transport package's chained <c>Use…Transport</c> on the builder, e.g.
/// <c>services.AddMosaic().UsePostgresTransport(connectionString)</c>.
/// </para>
/// </summary>
public sealed class InProcessOnlyTransport : IEventTransport
{
    public System.Threading.Tasks.ValueTask PublishAsync(
        string subject,
        MessageHeaders headers,
        ReadOnlySequence<byte> payload,
        System.Threading.CancellationToken cancellationToken = default)
        => default;
}
