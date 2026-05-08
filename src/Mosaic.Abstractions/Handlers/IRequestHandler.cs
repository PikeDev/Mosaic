namespace Mosaic;

/// <summary>
/// Handles a single <see cref="IRequest{TResponse}"/> and produces its response.
/// At most one implementation per request type — enforced at compile time by the source generator.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type carried by the request.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the request and returns the response.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="context">Composition context — gives access to events, nested composition, the active service provider, and the correlation ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<TResponse> Handle(
        TRequest request,
        ICompositionContext context,
        CancellationToken cancellationToken = default);
}
