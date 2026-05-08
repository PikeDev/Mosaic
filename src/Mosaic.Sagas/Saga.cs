namespace Mosaic.Sagas;

/// <summary>
/// Base class for a message-driven saga. The user's class declares the workflow via marker
/// interfaces (<see cref="IStartedBy{TMessage}"/>, <see cref="IHandles{TMessage}"/>) and writes one
/// <c>Handle(message, context, ct)</c> method per declared marker. The Mosaic source generator
/// reads the markers and emits the <see cref="IEventHandler{TEvent}"/> wrappers, finder lookups,
/// state guards (from <see cref="DuringAttribute"/>), and lifecycle plumbing.
/// <para>
/// Helpers on this base mutate <see cref="Data"/> directly — the source-gen-emitted wrapper saves
/// the consumer's DbContext after the handler returns, so transitions ride atomically with any
/// other state changes the handler made.
/// </para>
/// </summary>
/// <typeparam name="TData">The saga's state shape. Must inherit <see cref="SagaData"/>.</typeparam>
public abstract class Saga<TData> where TData : SagaData, new()
{
    /// <summary>The loaded (or freshly-created) saga state. The generated wrapper sets this before
    /// <c>Handle</c> runs; the setter is public for cross-assembly source-gen access but consumers
    /// shouldn't reassign it from their handler bodies.</summary>
    public TData Data { get; set; } = null!;

    /// <summary>
    /// Move the saga to a new state. Reads as <c>TransitionTo(OrderProcessState.ProcessingPayment)</c>
    /// at the call site.
    /// </summary>
    protected void TransitionTo(string state) => Data.CurrentState = state;

    /// <summary>
    /// Predicate for inline state checks: <c>if (When(OrderProcessState.ReservingInventory)) …</c>.
    /// Use <see cref="DuringAttribute"/> on a method to guard the whole handler instead.
    /// </summary>
    protected bool When(string state) => Data.CurrentState == state;

    /// <summary>
    /// Mark the saga finished. The generated wrapper deletes the row on save (default policy) or
    /// stamps <see cref="SagaData.CompletedAt"/> for audit (configurable).
    /// </summary>
    protected void Complete()
    {
        Data.IsCompleted = true;
        Data.CompletedAt = System.DateTime.UtcNow;
    }

    /// <summary>
    /// Schedule a timeout message for this saga. The message is dispatched after <paramref name="delay"/>
    /// via the configured <c>IScheduledMessageStore</c>; if the saga calls <see cref="Complete"/>
    /// before then, the generated lifecycle cancels the schedule. Dedup-keyed by saga id + message
    /// type, so re-scheduling the same timeout is idempotent.
    /// </summary>
    protected System.Threading.Tasks.ValueTask Schedule<TTimeout>(
        ICompositionContext context,
        System.TimeSpan delay,
        TTimeout timeout,
        System.Threading.CancellationToken cancellationToken = default)
        where TTimeout : IEvent
    {
        var dedupKey = SagaTimeoutKey.For<TTimeout>(Data.Id);
        return context.ScheduleMessage(delay, timeout, dedupKey, cancellationToken);
    }
}

/// <summary>
/// Conventional key shape used to identify a saga's outstanding timeout schedules so the generated
/// lifecycle can cancel them in bulk on <see cref="Saga{TData}.Complete"/>. The format
/// (<c>"saga:&lt;id&gt;:&lt;timeoutTypeFullName&gt;"</c>) is intentionally stable so external
/// observers (e.g. relays, dashboards) can reason about pending timeouts per saga.
/// </summary>
public static class SagaTimeoutKey
{
    public static string For<TTimeout>(System.Guid sagaId)
        => $"saga:{sagaId:N}:{typeof(TTimeout).FullName}";

    public static string PrefixFor(System.Guid sagaId)
        => $"saga:{sagaId:N}:";
}
