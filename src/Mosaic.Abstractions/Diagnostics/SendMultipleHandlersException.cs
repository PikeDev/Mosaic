namespace Mosaic;

/// <summary>
/// Thrown at startup (or at first dispatch in lazy-mode) if multiple <see cref="IRequestHandler{TRequest, TResponse}"/>
/// implementations are registered for the same request type. The compile-time <c>MOSAIC0002</c>
/// diagnostic should normally catch this earlier; this exception is the runtime safety net.
/// </summary>
public sealed class SendMultipleHandlersException(Type requestType, int handlerCount)
    : InvalidOperationException(
        $"{handlerCount} handlers were registered for request type {requestType.Name}; " +
        "exactly one is required. Mark all but one with [Lifetime(...)] removed or remove the " +
        "duplicate handler. The MOSAIC0002 diagnostic should have surfaced this at compile time.");
