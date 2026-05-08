namespace Mosaic;

/// <summary>
/// Delegate invoked by an <see cref="IPipelineBehavior{TRequest, TResponse}"/> to call the next
/// behavior in the chain (or the terminal handler).
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();
