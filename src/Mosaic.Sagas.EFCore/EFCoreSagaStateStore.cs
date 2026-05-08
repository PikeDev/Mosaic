using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Mosaic.Sagas;

namespace Mosaic.Sagas.EFCore;

/// <summary>
/// EF Core <see cref="ISagaStateStore{TData}"/> implementation. Resolves <typeparamref name="TDbContext"/>
/// from the same scope as the saga's request — so the saga's <see cref="ISagaStateStore{TData}.SaveChangesAsync"/>
/// flushes the saga's state alongside any other changes the handler (or the engine's outbox /
/// scheduled-message store) made on that DbContext, in one transaction.
/// </summary>
public sealed class EFCoreSagaStateStore<TDbContext,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces)]
    TData> : ISagaStateStore<TData>
    where TDbContext : DbContext
    where TData : SagaData, new()
{
    private readonly TDbContext _db;

    public EFCoreSagaStateStore(TDbContext db)
    {
        _db = db;
    }

    public async System.Threading.Tasks.Task<TData?> FindAsync(
        System.Linq.Expressions.Expression<System.Func<TData, bool>> predicate,
        System.Threading.CancellationToken cancellationToken)
        => await _db.Set<TData>().FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);

    public void Add(TData data) => _db.Add(data);

    public void Remove(TData data) => _db.Remove(data);

    public System.Threading.Tasks.Task SaveChangesAsync(System.Threading.CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);
}
