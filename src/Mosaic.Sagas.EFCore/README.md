# Mosaic.Sagas.EFCore

EF Core implementation of `ISagaStateStore<TData>` for [Mosaic](https://github.com/pikedev/mosaic) sagas. Auto-registered by the source generator for any saga whose primary constructor takes a `DbContext` parameter.

```csharp
public partial class BuyersRemorseSaga(SalesDbContext db) :
    Saga<BuyersRemorseSagaData>,
    IStartedBy<OrderPlaced>,
    IHandles<OrderCancellationRequested>
{
    public Task Handle(OrderPlaced m, ICompositionContext c, CancellationToken t) { ... }
    public Task Handle(OrderCancellationRequested m, ICompositionContext c, CancellationToken t) { ... }
}
```

When the generator detects a saga whose primary constructor takes a `DbContext`-derived type AND `Mosaic.Sagas.EFCore` is referenced, it emits `services.AddEFCoreSagaState<SalesDbContext, BuyersRemorseSagaData>()` automatically — no manual registration needed.

For sagas that use a different backend (Mongo, DynamoDB, in-memory for tests), implement `ISagaStateStore<TData>` yourself and register it manually; the generator will not auto-wire EF in that case.

The saga state row commits inside the consumer's own `DbContext.SaveChangesAsync`, so saga progression is atomic with whatever other state changes the handler made.

Pair with `Mosaic.Outbox.EFCore` for atomic publish and saga-timeout scheduling.
