namespace Mosaic;

/// <summary>
/// The composition engine — entry point for sending requests, composing view-models, and publishing events.
/// Resolved from DI as a singleton; per-invocation handler lifetimes are honored.
/// </summary>
public interface ICompositionEngine
{
    /// <summary>
    /// Dispatches a request to its single handler, optionally wrapped in pipeline behaviors.
    /// </summary>
    ValueTask<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fans out to all <see cref="IComposer{TRequest, TViewModel}"/> implementations for the
    /// request, allocating a fresh <typeparamref name="TViewModel"/> and returning the populated
    /// instance.
    /// </summary>
    ValueTask<TViewModel> Compose<TViewModel>(
        IComposable<TViewModel> request,
        CancellationToken cancellationToken = default)
        where TViewModel : new();

    /// <summary>
    /// Fans out to all composers using a pre-populated <typeparamref name="TViewModel"/> instance
    /// (e.g. when an upstream composer has already set the identifier or other initial fields).
    /// Useful for nested composition into a sub-VM.
    /// </summary>
    ValueTask Compose<TViewModel>(
        IComposable<TViewModel> request,
        TViewModel existing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an event to every <see cref="IEventHandler{TEvent}"/>. Default fan-out is parallel.
    /// <para>
    /// <paramref name="messageHeaders"/> identifies this message in the correlation graph.
    /// Top-level callers leave it null (the engine generates a fresh chain root); the framework
    /// passes its own <see cref="MessageHeaders"/> when chaining a publish from inside a handler
    /// (via <see cref="ICompositionContext.Publish{TEvent}"/>) or relaying an inbound transport
    /// delivery — keeps every link in the chain stitched by a stable <see cref="MessageHeaders.CorrelationId"/>.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last",
        Justification = "MessageHeaders must remain after CancellationToken so existing positional Publish(evt, ct) call sites stay source-compatible — the headers parameter is framework-internal and almost always passed by name.")]
    ValueTask Publish<TEvent>(
        TEvent notification,
        CancellationToken cancellationToken = default,
        MessageHeaders? messageHeaders = null)
        where TEvent : IEvent;
}
