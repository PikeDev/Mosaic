namespace Mosaic;

/// <summary>
/// Thrown at runtime if <see cref="ICompositionEngine.Compose{TViewModel}(IComposable{TViewModel}, System.Threading.CancellationToken)"/>
/// is invoked with a request type for which no composers were registered. The compile-time
/// <c>MOSAIC0003</c> diagnostic surfaces this earlier as a warning; if you've suppressed it,
/// this exception is the runtime fallback.
/// </summary>
public sealed class ComposeNoHandlerException(Type requestType, Type viewModelType)
    : InvalidOperationException(
        $"No IComposer<{requestType.Name}, {viewModelType.Name}> was registered. " +
        "Composing with zero composers is almost certainly a mistake. Ensure at least one " +
        "service project that contributes to this view-model is referenced by the composition root.");
