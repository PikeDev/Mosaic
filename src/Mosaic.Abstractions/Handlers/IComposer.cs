namespace Mosaic;

/// <summary>
/// Contributes a slice of a typed view-model in response to an <see cref="IComposable{TViewModel}"/>.
/// Many composers per request — they run in parallel and each mutates the shared view-model instance.
/// </summary>
/// <typeparam name="TRequest">The composable request type.</typeparam>
/// <typeparam name="TViewModel">The view-model being assembled.</typeparam>
/// <remarks>
/// The composer should mutate only its own section of the view-model. The convention is per-service
/// sub-objects on the parent VM, pre-allocated in the VM's constructor.
/// </remarks>
public interface IComposer<in TRequest, in TViewModel>
    where TRequest : IComposable<TViewModel>
{
    /// <summary>
    /// Contributes this composer's slice to the view-model.
    /// </summary>
    /// <param name="request">The composable request.</param>
    /// <param name="viewModel">The shared view-model being assembled. Mutate your slice in place.</param>
    /// <param name="context">Composition context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask Compose(
        TRequest request,
        TViewModel viewModel,
        ICompositionContext context,
        CancellationToken cancellationToken = default);
}
