using System.Buffers;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Mosaic;
using Mosaic.Sagas;

namespace Mosaic.Sample.SagaTimeout;

// In-memory infrastructure for the demo. Production replaces both with EF Core packages —
// see README's "What's stand-in vs production" section.

/// <summary>Demo-only saga state store. Production uses Mosaic.Sagas.EFCore.</summary>
public sealed class InMemorySagaState<TData> : ISagaStateStore<TData>
    where TData : SagaData, new()
{
    private readonly ConcurrentDictionary<Guid, TData> _byId = new();
    private readonly List<TData> _added = new();
    private readonly List<TData> _removed = new();

    public Task<TData?> FindAsync(Expression<Func<TData, bool>> predicate, CancellationToken cancellationToken)
    {
        var match = _byId.Values.AsQueryable().FirstOrDefault(predicate);
        return Task.FromResult(match);
    }

    public void Add(TData data) => _added.Add(data);
    public void Remove(TData data) => _removed.Add(data);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var d in _added) _byId[d.Id] = d;
        foreach (var d in _removed) _byId.TryRemove(d.Id, out _);
        _added.Clear();
        _removed.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Demo-only scheduler. Production uses <c>Mosaic.Outbox.EFCore</c>'s
/// <c>UseEFCoreScheduling&lt;TDbContext&gt;()</c>.
/// </summary>
public sealed class InMemoryScheduler : IScheduledMessageStore
{
    private sealed record Entry(string TypeFullName, MessageHeaders Headers, byte[] Payload, DateTime DueAtUtc, string DedupKey);
    private readonly ConcurrentDictionary<string, Entry> _pending = new();

    public ValueTask ScheduleAsync(string typeFullName, MessageHeaders headers, ReadOnlySequence<byte> payload, DateTime dueAtUtc, string dedupKey, CancellationToken cancellationToken)
    {
        _pending[dedupKey] = new Entry(typeFullName, headers, payload.ToArray(), dueAtUtc, dedupKey);
        return default;
    }

    public ValueTask<bool> CancelAsync(string dedupKey, CancellationToken cancellationToken)
        => new(_pending.TryRemove(dedupKey, out _));

    public ValueTask<int> CancelByPrefixAsync(string dedupKeyPrefix, CancellationToken cancellationToken)
    {
        var n = 0;
        foreach (var k in _pending.Keys.ToArray())
        {
            if (k.StartsWith(dedupKeyPrefix, StringComparison.Ordinal) && _pending.TryRemove(k, out _)) n++;
        }
        return new(n);
    }

    public async Task RunUntilEmptyAsync(IServiceProvider sp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_pending.IsEmpty)
        {
            var due = _pending.Values
                .Where(e => e.DueAtUtc <= DateTime.UtcNow)
                .OrderBy(e => e.DueAtUtc)
                .ToArray();
            foreach (var entry in due)
            {
                if (!_pending.TryRemove(entry.DedupKey, out _)) continue;
                using var scope = sp.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IInboundEventDispatcher>();
                await dispatcher.DispatchInboundAsync(entry.TypeFullName, entry.Headers, new ReadOnlySequence<byte>(entry.Payload), cancellationToken);
            }
            if (!_pending.IsEmpty) await Task.Delay(100, cancellationToken);
        }
    }
}
