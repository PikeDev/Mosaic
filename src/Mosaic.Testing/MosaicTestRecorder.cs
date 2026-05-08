using System.Collections.Concurrent;

namespace Mosaic.Testing;

/// <summary>
/// Internal store wired up via <see cref="ServiceCollectionExtensions.AddMosaicTestHarness"/> and
/// resolved by the recording behaviors. The <see cref="MosaicTestHarness"/> exposes the recorded
/// items via typed <see cref="RecordedMessages{T}"/> facades — those facades are LIVE views over
/// the recorder's underlying per-type queues, so a wait/poll on a facade reflects items recorded
/// after the facade was acquired (the relay's background dispatch is the typical case).
/// </summary>
public sealed class MosaicTestRecorder
{
    private readonly ConcurrentDictionary<Type, ConcurrentQueue<object>> _sent = new();
    private readonly ConcurrentDictionary<Type, ConcurrentQueue<object>> _published = new();
    private readonly ConcurrentDictionary<Type, ConcurrentQueue<object>> _composedRequests = new();
    private readonly ConcurrentDictionary<Type, ConcurrentQueue<object>> _composedResults = new();

    internal void RecordSent(object request)
        => _sent.GetOrAdd(request.GetType(), _ => new ConcurrentQueue<object>()).Enqueue(request);

    internal void RecordPublished(object notification)
        => _published.GetOrAdd(notification.GetType(), _ => new ConcurrentQueue<object>()).Enqueue(notification);

    internal void RecordComposed(object request, object viewModel)
        => _composedRequests.GetOrAdd(request.GetType(), _ => new ConcurrentQueue<object>()).Enqueue(request);

    internal void RecordComposedAfter(object viewModel)
        => _composedResults.GetOrAdd(viewModel.GetType(), _ => new ConcurrentQueue<object>()).Enqueue(viewModel);

    /// <summary>Get a live view of the recorded sends for <typeparamref name="TRequest"/>.</summary>
    public RecordedMessages<TRequest> Sent<TRequest>()
        => new(_sent.GetOrAdd(typeof(TRequest), _ => new ConcurrentQueue<object>()));

    /// <summary>Get a live view of the recorded events for <typeparamref name="TEvent"/>.</summary>
    public RecordedMessages<TEvent> Published<TEvent>() where TEvent : IEvent
        => new(_published.GetOrAdd(typeof(TEvent), _ => new ConcurrentQueue<object>()));

    /// <summary>Get a live view of the recorded composable requests of type <typeparamref name="TRequest"/>.</summary>
    public RecordedMessages<TRequest> Composed<TRequest>()
        => new(_composedRequests.GetOrAdd(typeof(TRequest), _ => new ConcurrentQueue<object>()));

    /// <summary>Get a live view of the populated view-models of type <typeparamref name="TViewModel"/>.</summary>
    public RecordedMessages<TViewModel> ComposedResults<TViewModel>()
        => new(_composedResults.GetOrAdd(typeof(TViewModel), _ => new ConcurrentQueue<object>()));

    /// <summary>Reset every recorded category. Useful between phases of a long-running test.</summary>
    public void Clear()
    {
        foreach (var q in _sent.Values) while (q.TryDequeue(out _)) { }
        foreach (var q in _published.Values) while (q.TryDequeue(out _)) { }
        foreach (var q in _composedRequests.Values) while (q.TryDequeue(out _)) { }
        foreach (var q in _composedResults.Values) while (q.TryDequeue(out _)) { }
    }
}
