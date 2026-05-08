using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mosaic.Runtime;

namespace Mosaic.Transport.InMemory;

/// <summary>
/// <see cref="IEventTransport"/> that routes events between DI containers in the same process via
/// a static, named channel. Each container resolves its own instance; they discover peers through
/// the channel registry and deliver published events to peer containers' inbound dispatchers.
/// <para>
/// Built for integration tests: spin up two containers (one publisher, one subscriber), give both
/// the same channel name, and assert the cascade end-to-end without standing up real infrastructure.
/// Loopback suppression by per-instance sender id — a publisher never receives its own events back.
/// </para>
/// <para>
/// Inbound dispatch goes through <see cref="InboundDispatch.TryDispatchAsync"/> so the same
/// retry + dead-letter behaviour applies as for real transports.
/// </para>
/// </summary>
public sealed class InMemoryEventTransport : IEventTransport, IAsyncDisposable
{
    private const string DefaultChannel = "default";

    // Channel name → live instances. Each instance subscribes itself in the ctor and unsubscribes
    // on disposal. Concurrent so containers can be created + disposed in parallel test runs.
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, InMemoryEventTransport>> _channels = new();

    private readonly string _channelName;
    private readonly string _senderId = Guid.NewGuid().ToString("N");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeadLetterStore _deadLetterStore;
    private readonly IRecoverabilityPolicy _recoverability;
    private readonly ICriticalErrorHandler _criticalError;
    private readonly ILogger<InMemoryEventTransport> _logger;
    private bool _disposed;

    public InMemoryEventTransport(
        IServiceScopeFactory scopeFactory,
        IDeadLetterStore deadLetterStore,
        IRecoverabilityPolicy recoverability,
        ICriticalErrorHandler criticalError,
        ILogger<InMemoryEventTransport> logger,
        string? channelName = null)
    {
        _scopeFactory = scopeFactory;
        _deadLetterStore = deadLetterStore;
        _recoverability = recoverability;
        _criticalError = criticalError;
        _logger = logger;
        _channelName = channelName ?? DefaultChannel;

        var subscribers = _channels.GetOrAdd(_channelName, _ => new ConcurrentDictionary<string, InMemoryEventTransport>());
        subscribers[_senderId] = this;
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync(string subject, MessageHeaders headers, ReadOnlySequence<byte> payload, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(_channelName, out var subscribers)) return;

        // Materialise the payload once — peers each get their own ReadOnlySequence over the same
        // immutable byte array so concurrent dispatches don't fight over a shared advancing cursor.
        var bytes = payload.ToArray();

        // Snapshot so a peer disposing mid-fan-out doesn't perturb the iteration. Loopback drop
        // by sender id — a container never receives its own publishes (matches Postgres semantics).
        var peers = subscribers.Values.Where(s => s._senderId != _senderId).ToArray();
        foreach (var peer in peers)
        {
            await peer.HandleInboundAsync(subject, headers, new ReadOnlySequence<byte>(bytes), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Invoked by a peer's <see cref="PublishAsync"/>. Uses the receiving container's resilience
    /// settings (its own retry policy, its own dead-letter store) — not the publisher's.
    /// </summary>
    private Task HandleInboundAsync(string typeFullName, MessageHeaders headers, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
        => InboundDispatch.TryDispatchAsync(typeFullName, headers, payload, _scopeFactory, _deadLetterStore, _recoverability, _criticalError, _logger, cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        if (_channels.TryGetValue(_channelName, out var subscribers))
        {
            subscribers.TryRemove(_senderId, out _);
        }
        return ValueTask.CompletedTask;
    }
}
