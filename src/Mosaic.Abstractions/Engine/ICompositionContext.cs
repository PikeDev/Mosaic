namespace Mosaic;

/// <summary>
/// Per-invocation context passed to every handler, composer, and event handler.
/// Provides correlation tracing, the active <see cref="IServiceProvider"/>, and re-entrant access
/// to the engine for raising events, sending nested requests, and composing nested view-models —
/// all without injecting the engine directly.
/// </summary>
public interface ICompositionContext
{
    /// <summary>
    /// Identifier flowing through the entire composition, surfaced on activity tags and audit logs.
    /// Defaults to the current <see cref="System.Diagnostics.Activity.TraceId"/> when one exists,
    /// otherwise a freshly generated GUID.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// The identifier of the message whose processing produced this one (the "RelatedTo" header).
    /// Null at the top of a chain.
    /// </summary>
    string? CausationId { get; }

    /// <summary>
    /// The active service provider for this composition. Honors the call-site's DI scope —
    /// when the engine is invoked from inside an HTTP request scope, this is the request scope's provider.
    /// </summary>
    IServiceProvider Services { get; }

    // ─── Events ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Publishes an event to every <see cref="IEventHandler{TEvent}"/>. By default, events
    /// published from inside a composition buffer until the parent composition completes
    /// (outbox-style); override per-assembly via <see cref="CompositionConfigurationAttribute.EventPublishMode"/>.
    /// </summary>
    ValueTask Publish<TEvent>(
        TEvent notification,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// Subscribes an inline (lambda) handler for an event, scoped to <b>this composition only</b>.
    /// Useful when one composer wants to react to events from peers without a dedicated handler class.
    /// </summary>
    void Subscribe<TEvent>(EventCallback<TEvent> handler) where TEvent : IEvent;

    // ─── Scheduled messages ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Schedule <paramref name="message"/> for delivery after <paramref name="delay"/>. When the
    /// time arrives the message is dispatched to its in-process <see cref="IEventHandler{TEvent}"/>s
    /// exactly as if it had just been published — useful for saga timeouts (buyer's-remorse hold,
    /// gateway-retry windows, fraud-review SLA).
    /// <para>
    /// <paramref name="dedupKey"/> identifies the schedule for later <see cref="CancelScheduledMessage"/>.
    /// Pass a domain key (e.g. <c>$"buyers-remorse:{orderId}"</c>) when the saga may complete early
    /// and need to cancel the timeout. Pass <c>null</c> for fire-and-forget.
    /// </para>
    /// <para>
    /// Throws when no <see cref="IScheduledMessageStore"/> is registered — wire one via
    /// <c>UseEFCoreScheduling&lt;TDbContext&gt;()</c> or an equivalent adapter.
    /// </para>
    /// </summary>
    ValueTask ScheduleMessage<TMessage>(
        System.TimeSpan delay,
        TMessage message,
        string? dedupKey = null,
        CancellationToken cancellationToken = default)
        where TMessage : IEvent;

    /// <summary>
    /// Cancel a still-pending scheduled message by its <paramref name="dedupKey"/>. Returns
    /// <c>true</c> if it was cancelled, <c>false</c> if it had already fired or never existed.
    /// </summary>
    ValueTask<bool> CancelScheduledMessage(
        string dedupKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Route an orphaned <paramref name="message"/> (whose target saga / aggregate didn't exist)
    /// to an opt-in <see cref="IHandleSagaNotFound{TMessage}"/> if one is registered for
    /// <typeparamref name="TMessage"/>. No-op (debug-logged) when no handler is registered.
    /// <para>
    /// Handlers call this from their early-return branches: <c>if (order is null) { await
    /// context.HandleSagaNotFoundAsync(notification, ct); return; }</c>. Turns silent orphan
    /// drops into a typed, debuggable seam.
    /// </para>
    /// </summary>
    ValueTask HandleSagaNotFoundAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
        where TMessage : IEvent;

    // ─── Nested send / compose ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a nested request, dispatching to its single handler. Runs in the same DI scope as the parent.
    /// </summary>
    ValueTask<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes a nested view-model. Allocates a fresh instance, runs all composers, returns it.
    /// Runs in the same DI scope as the parent.
    /// </summary>
    ValueTask<TViewModel> Compose<TViewModel>(
        IComposable<TViewModel> request,
        CancellationToken cancellationToken = default)
        where TViewModel : new();

    /// <summary>
    /// Composes a nested view-model into a pre-populated instance — fans out all composers against
    /// the supplied <typeparamref name="TViewModel"/>. Use when the parent composer has already
    /// initialised some fields (e.g. an identifier).
    /// </summary>
    ValueTask Compose<TViewModel>(
        IComposable<TViewModel> request,
        TViewModel existing,
        CancellationToken cancellationToken = default);
}
