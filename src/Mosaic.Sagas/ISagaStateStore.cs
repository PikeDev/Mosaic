namespace Mosaic.Sagas;

/// <summary>
/// Persistence abstraction for saga state. The source-generated saga wrapper resolves this from
/// DI per dispatch, looks up the state by correlation, hands the loaded state to the user's
/// <c>Handle</c> method, and calls <see cref="SaveChangesAsync"/> at end-of-handler so the saga's
/// progression rides the same transaction as any other state changes the handler made (the EF
/// adapter's <c>SaveChanges</c> flushes the consumer's whole DbContext).
/// <para>
/// Mosaic.Sagas itself is storage-agnostic: ship <c>Mosaic.Sagas.EFCore</c> for the EF Core
/// adapter (auto-registered when a saga's primary constructor accepts a <c>DbContext</c>), or
/// implement <see cref="ISagaStateStore{TData}"/> against a different store (Mongo, DynamoDB,
/// in-memory for tests, etc.) and register it manually:
/// <code>
/// services.AddScoped&lt;ISagaStateStore&lt;MySagaData&gt;, MyCustomStore&gt;();
/// </code>
/// </para>
/// </summary>
/// <typeparam name="TData">The saga's state shape — must inherit <see cref="SagaData"/>.</typeparam>
public interface ISagaStateStore<TData> where TData : SagaData, new()
{
    /// <summary>
    /// Find a saga state row matching the predicate (a correlation lookup). Returns null when no
    /// row matches — the wrapper then either creates a fresh one (starter messages) or routes to
    /// <see cref="ICompositionContext.HandleSagaNotFoundAsync{TMessage}"/>.
    /// </summary>
    System.Threading.Tasks.Task<TData?> FindAsync(
        System.Linq.Expressions.Expression<System.Func<TData, bool>> predicate,
        System.Threading.CancellationToken cancellationToken);

    /// <summary>Track a freshly-created saga state row for insertion at next save.</summary>
    void Add(TData data);

    /// <summary>Mark a saga state row for deletion at next save (called when the saga calls <see cref="Saga{TData}.Complete"/>).</summary>
    void Remove(TData data);

    /// <summary>Persist all tracked changes — adds, removes, and mutations on the loaded entity.</summary>
    System.Threading.Tasks.Task SaveChangesAsync(System.Threading.CancellationToken cancellationToken);
}
