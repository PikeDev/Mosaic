namespace Mosaic;

/// <summary>
/// Cross-cutting middleware wrapping the dispatch of an <see cref="IRequest{TResponse}"/>.
/// Behaviors are unrolled per concrete request/response pair by the source generator;
/// ordering is set once at the assembly level via <see cref="CompositionConfigurationAttribute.PipelineBehaviors"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Typical implementations: logging, validation, retries, transactions, caching, telemetry.
/// </remarks>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Wraps the next behavior (or the terminal handler) in the chain.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="nextHandler">Delegate that invokes the next behavior or the terminal handler.</param>
    /// <param name="context">Composition context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> nextHandler,
        ICompositionContext context,
        CancellationToken cancellationToken = default);
}
