namespace Mosaic;

/// <summary>
/// Cross-cutting middleware wrapping the dispatch of an <see cref="IComposable{TViewModel}"/>.
/// Behaviors run once per <see cref="ICompositionEngine.Compose"/> call (around the entire
/// composer fan-out), not once per individual composer — so logging, validation, caching,
/// or "wrap the resulting VM" concerns can sit at one layer regardless of how many composers
/// contribute.
/// <para>
/// Ordering is set once at the assembly level via
/// <see cref="CompositionConfigurationAttribute.ComposeBehaviors"/>; each entry must be an
/// open-generic implementation of this interface (e.g. <c>typeof(LoggingComposeBehavior&lt;,&gt;)</c>),
/// which the source generator closes over each concrete (TRequest, TViewModel) pair at compile time.
/// </para>
/// </summary>
/// <typeparam name="TRequest">The composable request type.</typeparam>
/// <typeparam name="TViewModel">The view-model type the composers contribute to.</typeparam>
public interface IComposeBehavior<in TRequest, in TViewModel>
    where TRequest : IComposable<TViewModel>
{
    /// <summary>
    /// Wraps the next behavior (or the terminal composer fan-out) in the chain. The
    /// <paramref name="viewModel"/> is the same instance the composers will write to and that
    /// will be returned to the caller — mutating its fields here is allowed.
    /// </summary>
    ValueTask Handle(
        TRequest request,
        TViewModel viewModel,
        ComposeHandlerDelegate nextHandler,
        ICompositionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Delegate invoked by an <see cref="IComposeBehavior{TRequest, TViewModel}"/> to call the next
/// behavior in the chain — or the terminal composer fan-out.
/// </summary>
public delegate ValueTask ComposeHandlerDelegate();
