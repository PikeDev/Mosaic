namespace Mosaic;

/// <summary>
/// Thrown at runtime if <see cref="ICompositionEngine.Send{TResponse}"/> is invoked with a request
/// type for which no handler was registered. This indicates the source generator did not see a handler
/// — typically because the consuming project was not referenced by the composition root.
/// </summary>
public sealed class SendNoHandlerException(Type requestType)
    : InvalidOperationException(
        $"No IRequestHandler<{requestType.Name}, _> was registered. " +
        "Ensure the project containing the handler is referenced by the composition root.");
