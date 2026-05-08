using System.Collections.Concurrent;

namespace Mosaic.Testing;

/// <summary>
/// Thread-safe live view over recorded test messages with assertion + wait helpers. Each
/// <see cref="MosaicTestHarness"/> exposes one of these per category — sent requests, composed
/// view-models, published events. Backed by the recorder's underlying queue, so reads always
/// reflect the current state — calling <see cref="WaitForAsync(int, TimeSpan?, CancellationToken)"/>
/// works even when the items arrive after the call (e.g. from a relay-dispatched timeout).
/// </summary>
public sealed class RecordedMessages<T>
{
    private readonly ConcurrentQueue<object> _source;

    /// <summary>Construct a typed live view over the recorder's underlying per-type queue.</summary>
    internal RecordedMessages(ConcurrentQueue<object> source)
    {
        _source = source;
    }

    internal void Record(T item) => _source.Enqueue(item!);

    /// <summary>Snapshot of every recorded item of type <typeparamref name="T"/>, oldest first.</summary>
    public IReadOnlyList<T> All => Filtered();

    /// <summary>Count of recorded items of type <typeparamref name="T"/>.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (var item in _source) if (item is T) n++;
            return n;
        }
    }

    /// <summary>Shorthand for <c>All.OfType&lt;TConcrete&gt;()</c>.</summary>
    public IReadOnlyList<TConcrete> Of<TConcrete>() where TConcrete : T
    {
        var list = new List<TConcrete>();
        foreach (var item in _source) if (item is TConcrete c) list.Add(c);
        return list;
    }

    /// <summary>Wait for at least <paramref name="count"/> recorded items, or fail by timeout.</summary>
    public async Task<IReadOnlyList<T>> WaitForAsync(
        int count = 1,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = Filtered();
            if (snapshot.Count >= count) return snapshot;

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50, remaining.TotalMilliseconds)), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out waiting for {count} item(s) of type {typeof(T).Name}; observed {Count}.");
    }

    /// <summary>Wait for at least one recorded item matching <paramref name="predicate"/>.</summary>
    public async Task<T> WaitForAsync(
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in _source)
            {
                if (item is T t && predicate(t)) return t;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50, remaining.TotalMilliseconds)), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out waiting for an item of type {typeof(T).Name} matching the predicate; observed {Count}.");
    }

    /// <summary>Reset the recorded items (between phases of a long test). Empties the underlying queue.</summary>
    public void Clear()
    {
        while (_source.TryDequeue(out _)) { }
    }

    private List<T> Filtered()
    {
        var list = new List<T>();
        foreach (var item in _source) if (item is T t) list.Add(t);
        return list;
    }
}
