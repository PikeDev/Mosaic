using Microsoft.Extensions.DependencyInjection;

namespace Mosaic.Testing;

/// <summary>
/// Recording <see cref="IPipelineBehavior{TRequest, TResponse}"/>. Captures requests when a
/// <see cref="MosaicTestRecorder"/> is registered; degrades to a transparent no-op when not.
/// Safe to leave wired in your assembly's <c>[CompositionConfiguration(PipelineBehaviors = …)]</c>
/// permanently — production runs that don't register a recorder pay nothing.
/// </summary>
public sealed class RecordingPipelineBehavior<TRequest, TResponse>(IServiceProvider services) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly MosaicTestRecorder? _recorder = services.GetService<MosaicTestRecorder>();

    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> nextHandler,
        ICompositionContext context,
        CancellationToken cancellationToken = default)
    {
        _recorder?.RecordSent(request!);
        return await nextHandler().ConfigureAwait(false);
    }
}

/// <summary>
/// Recording <see cref="IPublishBehavior{TEvent}"/>. Captures events when a
/// <see cref="MosaicTestRecorder"/> is registered; degrades to a transparent no-op when not.
/// </summary>
public sealed class RecordingPublishBehavior<TEvent>(IServiceProvider services) : IPublishBehavior<TEvent>
    where TEvent : IEvent
{
    private readonly MosaicTestRecorder? _recorder = services.GetService<MosaicTestRecorder>();

    public ValueTask Handle(
        TEvent notification,
        PublishHandlerDelegate nextHandler,
        ICompositionContext context,
        CancellationToken cancellationToken = default)
    {
        _recorder?.RecordPublished(notification!);
        return nextHandler();
    }
}

/// <summary>
/// Recording <see cref="IComposeBehavior{TRequest, TViewModel}"/>. Captures the request before the
/// chain and the populated view-model after — so <c>harness.Composed&lt;Req&gt;()</c> sees the
/// inputs and <c>harness.ComposedResults&lt;Vm&gt;()</c> sees the populated outputs.
/// </summary>
public sealed class RecordingComposeBehavior<TRequest, TViewModel>(IServiceProvider services) : IComposeBehavior<TRequest, TViewModel>
    where TRequest : IComposable<TViewModel>
{
    private readonly MosaicTestRecorder? _recorder = services.GetService<MosaicTestRecorder>();

    public async ValueTask Handle(
        TRequest request,
        TViewModel viewModel,
        ComposeHandlerDelegate nextHandler,
        ICompositionContext context,
        CancellationToken cancellationToken = default)
    {
        _recorder?.RecordComposed(request!, viewModel!);
        await nextHandler().ConfigureAwait(false);
        _recorder?.RecordComposedAfter(viewModel!);
    }
}
