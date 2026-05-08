using System.Diagnostics;

namespace Mosaic.Runtime;

/// <summary>
/// Per-invocation implementation of <see cref="ICompositionContext"/>. Constructed by source-generated
/// dispatch code; not intended to be used directly by handlers (they receive it as
/// <see cref="ICompositionContext"/>).
/// </summary>
/// <remarks>
/// Implements buffered event semantics by default — events raised inside a composition are
/// queued on the context and flushed via <see cref="FlushBufferedEventsAsync"/> after the parent
/// composition completes.
/// </remarks>
public sealed class CompositionContext : ICompositionContext
{
    private readonly ICompositionEngine _engine;
    private readonly Dictionary<Type, List<Delegate>> _inlineSubscriptions = new();
    private readonly List<Func<CancellationToken, ValueTask>> _bufferedEvents = new();
    private readonly EventPublishMode _publishMode;
    private readonly IOutboxBuffer? _outboxBuffer;
    private readonly bool _isInboundContext;

    public CompositionContext(
        IServiceProvider services,
        ICompositionEngine engine,
        EventPublishMode publishMode = EventPublishMode.Buffered,
        MessageHeaders? inboundHeaders = null)
    {
        Services = services;
        _engine = engine;
        _publishMode = publishMode;
        // If an atomic-outbox adapter (e.g. Mosaic.Outbox.EFCore) registered a buffer in DI,
        // events flow through it as well as the in-process path. The buffer drains atomically
        // with the consumer's commit; the in-process dispatch still fires for local subscribers.
        _outboxBuffer = (IOutboxBuffer?)services.GetService(typeof(IOutboxBuffer));

        // Adopt the inbound message's headers so any cascaded publishes inherit the same
        // CorrelationId and chain via CausationId. Roots get a fresh chain seeded from
        // the current Activity (so distributed-tracing TraceId and Mosaic correlation align).
        _isInboundContext = inboundHeaders is not null;
        if (inboundHeaders is { } h)
        {
            MessageId = h.MessageId;
            CorrelationId = h.CorrelationId;
            CausationId = h.CausationId;
        }
        else
        {
            MessageId = Guid.NewGuid();
            CorrelationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            CausationId = null;
        }
    }

    /// <summary>
    /// MessageId of the message currently being processed by this context. For root dispatches
    /// (initial Send/Publish from outside a handler), a fresh GUID is generated and becomes the
    /// chain's seed — every cascaded publish stamps its own <c>CausationId</c> with this value.
    /// </summary>
    public Guid MessageId { get; }

    public string CorrelationId { get; }
    public string? CausationId { get; }
    public IServiceProvider Services { get; }

    /// <summary>
    /// Build the wire <see cref="MessageHeaders"/> for a brand-new message published or scheduled
    /// from this context.
    /// <list type="bullet">
    ///   <item>If this context is processing a real inbound message (transport delivery or chained
    ///         publish), the new message is that message's child: shares <see cref="CorrelationId"/>,
    ///         <c>CausationId</c> points back to <see cref="MessageId"/>.</item>
    ///   <item>If this context is a synthetic root (top-level engine call from outside any handler),
    ///         the new message becomes a chain root itself — fresh <see cref="MessageId"/>, no
    ///         causation. The synthetic context's <see cref="MessageId"/> is never written to the
    ///         audit trail, so it must not appear as a parent.</item>
    /// </list>
    /// Public because the source-generated dispatch code (in the consumer's assembly) calls it on
    /// every outbound publish to stitch the correlation chain.
    /// </summary>
    public MessageHeaders NewOutboundHeaders()
    {
        var causation = _isInboundContext ? MessageId.ToString("N") : (string?)null;
        return new MessageHeaders(Guid.NewGuid(), CorrelationId, causation, DateTime.UtcNow);
    }

    public ValueTask Publish<TEvent>(TEvent notification, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        var inlineTask = InvokeInlineSubscriptions(notification, cancellationToken);

        // Stamp the new message as a child of the message this context is processing — the
        // CorrelationId stays stable across the chain, CausationId points back to the parent.
        var headers = NewOutboundHeaders();

        // Atomic-outbox path: when an IOutboxBuffer is registered the engine's IEventTransport is
        // the OutboxRoutingTransport — calling _engine.Publish enqueues the row into the consumer's
        // DbContext change tracker via the transport, so the next SaveChanges commits state +
        // outbox atomically. Force eager dispatch so the enqueue lands BEFORE any subsequent
        // SaveChanges the handler does (buffered would defer it past the Save and break atomicity).
        if (_outboxBuffer is not null)
        {
            return Combine(inlineTask, _engine.Publish(notification, cancellationToken, headers));
        }

        if (_publishMode == EventPublishMode.Eager)
        {
            // Dispatch immediately via the generic engine method (the generated switch
            // statement picks the right handler-fan-out per concrete TEvent).
            return Combine(inlineTask, _engine.Publish(notification, cancellationToken, headers));
        }

        // Buffered (default): queue dispatch until the parent composition completes
        _bufferedEvents.Add(ct => _engine.Publish(notification, ct, headers));
        return inlineTask;
    }

    public ValueTask ScheduleMessage<TMessage>(
        TimeSpan delay,
        TMessage message,
        string? dedupKey = null,
        CancellationToken cancellationToken = default)
        where TMessage : IEvent
    {
        var store = (IScheduledMessageStore?)Services.GetService(typeof(IScheduledMessageStore))
            ?? throw new InvalidOperationException(
                "ICompositionContext.ScheduleMessage requires an IScheduledMessageStore. " +
                "Wire one via .UseEFCoreScheduling<TDbContext>() (Mosaic.Outbox.EFCore) or a custom adapter.");

        var registry = (IMosaicSerializerRegistry?)Services.GetService(typeof(IMosaicSerializerRegistry))
            ?? throw new InvalidOperationException(
                "ICompositionContext.ScheduleMessage requires an IMosaicSerializerRegistry. " +
                "AddMosaic() registers the System.Text.Json default; if you replaced it, ensure your " +
                "registry is still in DI.");

        var typeName = typeof(TMessage).FullName ?? typeof(TMessage).Name;
        var serializer = registry.GetSerializer<TMessage>();
        var dueAt = DateTime.UtcNow.Add(delay);
        var key = dedupKey ?? Guid.NewGuid().ToString("N");
        var headers = NewOutboundHeaders();
        return ScheduleAndReturnBufferAsync(store, serializer, message, typeName, headers, dueAt, key, cancellationToken);
    }

    private static async ValueTask ScheduleAndReturnBufferAsync<TMessage>(
        IScheduledMessageStore store,
        IMosaicSerializer<TMessage> serializer,
        TMessage message,
        string typeName,
        MessageHeaders headers,
        DateTime dueAtUtc,
        string dedupKey,
        CancellationToken cancellationToken)
    {
        // Pooled buffer must outlive the await — IScheduledMessageStore implementations copy the
        // payload to their own storage before returning, so disposing here is safe.
        using var writer = MosaicBufferWriter.Rent();
        serializer.Serialize(writer, message);
        var payload = new System.Buffers.ReadOnlySequence<byte>(writer.WrittenMemory);
        await store.ScheduleAsync(typeName, headers, payload, dueAtUtc, dedupKey, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> CancelScheduledMessage(string dedupKey, CancellationToken cancellationToken = default)
    {
        var store = (IScheduledMessageStore?)Services.GetService(typeof(IScheduledMessageStore))
            ?? throw new InvalidOperationException(
                "ICompositionContext.CancelScheduledMessage requires an IScheduledMessageStore.");
        return store.CancelAsync(dedupKey, cancellationToken);
    }

    public ValueTask HandleSagaNotFoundAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : IEvent
    {
        var handler = (IHandleSagaNotFound<TMessage>?)Services.GetService(typeof(IHandleSagaNotFound<TMessage>));
        if (handler is not null)
        {
            return handler.OnNotFoundAsync(message, this, cancellationToken);
        }
        // No opt-in handler registered: emit a debug log via the loggerfactory if available.
        var loggerFactory = (Microsoft.Extensions.Logging.ILoggerFactory?)Services.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory));
        Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(
            loggerFactory?.CreateLogger("Mosaic.SagaNotFound")
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            "Saga-not-found for {Type}: no IHandleSagaNotFound<{Type}> registered; ignoring.",
            typeof(TMessage).FullName ?? typeof(TMessage).Name,
            typeof(TMessage).Name);
        return default;
    }

    public void Subscribe<TEvent>(EventCallback<TEvent> handler) where TEvent : IEvent
    {
        if (!_inlineSubscriptions.TryGetValue(typeof(TEvent), out var list))
        {
            list = [];
            _inlineSubscriptions[typeof(TEvent)] = list;
        }
        list.Add(handler);
    }

    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => _engine.Send(request, cancellationToken);

    public ValueTask<TViewModel> Compose<TViewModel>(IComposable<TViewModel> request, CancellationToken cancellationToken = default)
        where TViewModel : new()
        => _engine.Compose(request, cancellationToken);

    public ValueTask Compose<TViewModel>(IComposable<TViewModel> request, TViewModel existing, CancellationToken cancellationToken = default)
        => _engine.Compose(request, existing, cancellationToken);

    /// <summary>
    /// Flushes any buffered events. Called by the source-generated engine after the parent
    /// composition completes. Idempotent — buffered list is cleared on each call.
    /// </summary>
    public async ValueTask FlushBufferedEventsAsync(CancellationToken cancellationToken)
    {
        if (_bufferedEvents.Count == 0)
        {
            return;
        }

        // Snapshot so handlers raising further events don't mutate the list we're iterating
        var snapshot = _bufferedEvents.ToArray();
        _bufferedEvents.Clear();

        foreach (var dispatcher in snapshot)
        {
            await dispatcher(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Helper invoked from the source-generated event-publish dispatcher. Awaits the handler's
    /// invocation and then flushes any events the handler buffered onto its (sub-scope) context.
    /// Public because the generated code lives in the consumer's compilation and needs to call it.
    /// </summary>
    public static async global::System.Threading.Tasks.Task HandleAndFlushAsync(
        global::System.Threading.Tasks.Task handlerInvocation,
        CompositionContext context,
        CancellationToken cancellationToken)
    {
        await handlerInvocation.ConfigureAwait(false);
        await context.FlushBufferedEventsAsync(cancellationToken).ConfigureAwait(false);
    }

    private ValueTask InvokeInlineSubscriptions<TEvent>(TEvent notification, CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        if (!_inlineSubscriptions.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
        {
            return default;
        }

        var tasks = new Task[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            var typed = (EventCallback<TEvent>)list[i];
            tasks[i] = typed(notification, this, cancellationToken).AsTask();
        }
        return new ValueTask(Task.WhenAll(tasks));
    }

    private static ValueTask Combine(ValueTask first, ValueTask second)
    {
        if (first.IsCompletedSuccessfully && second.IsCompletedSuccessfully)
        {
            return default;
        }
        return new ValueTask(Task.WhenAll(first.AsTask(), second.AsTask()));
    }
}
