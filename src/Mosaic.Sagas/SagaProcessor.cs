namespace Mosaic.Sagas;

/// <summary>
/// Base class for a polled saga of state rows of type <typeparamref name="TState"/>.
/// <para>
/// Implement <see cref="FindActiveAsync"/> to return rows that need progressing, and
/// <see cref="ProcessAsync"/> to drive one row forward. The Mosaic saga host calls
/// <see cref="ProcessOnceAsync"/> on the cadence reported by <see cref="PollInterval"/>;
/// each cycle runs in a fresh DI scope so the consumer's scoped DbContext + composition engine
/// are isolated from prior cycles.
/// </para>
/// <para>
/// Persistence is the consumer's concern. The state rows passed to <see cref="ProcessAsync"/>
/// are typically tracked entities loaded by <see cref="FindActiveAsync"/>; mutating them and
/// calling <c>SaveChangesAsync</c> on the resolved DbContext persists changes naturally.
/// </para>
/// </summary>
public abstract class SagaProcessor<TState> : ISagaProcessor where TState : class
{
    /// <inheritdoc />
    public abstract System.TimeSpan PollInterval { get; }

    /// <inheritdoc />
    public virtual string Name => GetType().Name;

    /// <summary>Returns the saga state rows that need progressing this cycle.</summary>
    protected abstract System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<TState>> FindActiveAsync(
        System.IServiceProvider scope,
        System.Threading.CancellationToken cancellationToken);

    /// <summary>Drives a single state row forward. Mutate state, save, publish events as needed.</summary>
    protected abstract System.Threading.Tasks.Task ProcessAsync(
        TState state,
        System.IServiceProvider scope,
        System.Threading.CancellationToken cancellationToken);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task ProcessOnceAsync(
        System.IServiceProvider scope,
        System.Threading.CancellationToken cancellationToken)
    {
        var active = await FindActiveAsync(scope, cancellationToken).ConfigureAwait(false);
        if (active.Count == 0) return;
        foreach (var state in active)
        {
            await ProcessAsync(state, scope, cancellationToken).ConfigureAwait(false);
        }
    }
}
