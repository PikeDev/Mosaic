# Mosaic.Sagas

Two flavours of saga, sharing one package:

- **`Saga<TData>`** — message-driven sagas. Marker interfaces declare what kicks the saga off and what it reacts to; the source generator emits the dispatch wrappers; you write only the workflow.
- **`SagaProcessor<TState>`** — polled, time-driven sagas. For workflows that progress on a schedule independently of incoming messages (rare in practice; most timeouts are better expressed as scheduled messages on a `Saga<TData>`).

Most workflows want `Saga<TData>`. The rest of this README covers it; `SagaProcessor<TState>` lives at the bottom.

---

## Quick example

```csharp
public sealed class PaymentAuthorizationData : SagaData
{
    [Correlation]
    public OrderId OrderId { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string? GatewayUsed { get; set; }
}

public partial class PaymentAuthorizationSaga(
    BillingDbContext db,
    IPrimaryPaymentGateway primary,
    IFallbackPaymentGateway fallback,
    TimeProvider time,
    ILogger<PaymentAuthorizationSaga> logger) :
    Saga<PaymentAuthorizationData>,
    IStartedBy<OrderAccepted>
{
    public async Task Handle(OrderAccepted message, ICompositionContext context, CancellationToken ct)
    {
        Data.OrderId = message.OrderId;
        Data.Amount = message.Total;
        Data.Currency = message.Currency;

        var primaryRes = await primary.AuthorizeAsync(Data.OrderId, Data.Amount, Data.Currency, ct);
        if (primaryRes.Success)
        {
            await Authorize(context, "primary", primaryRes.Reference!, ct);
            return;
        }

        var fallbackRes = await fallback.AuthorizeAsync(Data.OrderId, Data.Amount, Data.Currency, ct);
        if (fallbackRes.Success)
        {
            await Authorize(context, "fallback", fallbackRes.Reference!, ct);
            return;
        }

        await context.Publish(new PaymentAuthorizationFailed(Data.OrderId), ct);
        Complete();
    }

    private async Task Authorize(ICompositionContext context, string used, string reference, CancellationToken ct)
    {
        Data.GatewayUsed = used;
        await context.Publish(new PaymentAuthorized(Data.OrderId, used, reference), ct);
        Complete();
    }
}
```

That's the entire saga. The class declaration tells the story: *"Started by `OrderAccepted`. The body runs primary→fallback inline and publishes the outcome."* No `ConfigureFinder`, no state-machine DSL, no separate `IEventHandler<OrderAccepted>` registration — the source generator emits all of that.

---

## The vocabulary

| Concept | Surface |
|---|---|
| Saga base class | `Saga<TData>` |
| State data base | `SagaData` (provides `Id`, `CurrentState`, `IsCompleted`, `CompletedAt`) |
| Saga is started by an event | `IStartedBy<TMessage>` marker |
| Saga continues on an event | `IHandles<TMessage>` marker |
| Correlation property | `[Correlation]` attribute on a `TData` property |
| State-bound dispatch | `[During(state, …)]` attribute on a `Handle` method |
| Custom correlation | `static partial Guid CorrelateBy(TMessage m) => …` (escape hatch) |
| Handler method | `Handle(message, context, ct)` |

State helpers on `Saga<TData>`:

| Method | Effect |
|---|---|
| `Data` | The loaded (or freshly-created) `TData` row. Mutate fields directly; the wrapper saves on return. |
| `TransitionTo(state)` | Sets `Data.CurrentState`. Reads as *"transition to ProcessingPayment"*. |
| `When(state)` | Inline predicate. `if (When(...)) …`. Use for branching inside one handler. |
| `Complete()` | Marks the saga done. The wrapper deletes the row + cancels pending timeouts on save. |
| `Schedule<TTimeout>(context, delay, instance, ct)` | Schedules a timeout, dedup-keyed by saga id + timeout type. |

---

## Correlation

The source generator builds the finder by **convention**:

1. Reads `TData`'s `[Correlation]` property (or falls back to `Id` when nothing is marked).
2. For each declared marker, looks for a property with the same name + same type on the message.
3. If found, emits `db.Set<TData>().FirstOrDefaultAsync(d => d.OrderId == message.OrderId, ct)` for the lookup.
4. If not found, the build fails with `MOSAIC_SAGA_003` pointing at the message — fix by renaming the property *or* adding the partial-method override:

```csharp
public partial class OrderProcessSaga
{
    private static partial Guid CorrelateBy(LegacyOrderEvent m) => m.LegacyOrderId;
}
```

When the partial is present, the generator uses it instead of convention.

---

## State-bound dispatch with `[During]`

`[During(state, …)]` on a `Handle` method emits an early-return guard at the top of the wrapped call. The handler runs only when `Data.CurrentState` matches one of the listed states; otherwise the wrapper returns silently.

```csharp
[During(OrderProcessState.ProcessingPayment)]
public async Task Handle(PaymentProcessed message, ICompositionContext context, CancellationToken ct)
{
    await context.Publish(new ReserveInventory(Data.OrderId), ct);
    TransitionTo(OrderProcessState.ReservingInventory);
}
```

Multi-state form: `[During(StateA, StateB)]`. For finer-grained branching inside one handler, use the inline `When(state)` predicate.

C# only allows one method per `(name, signature)` pair, so a single message handled differently per state is one method with internal `if (When(...))` branches — not two methods with different `[During]` attributes. If that becomes painful for a particular saga, file an issue; an attribute-driven multi-method dispatch (`[OnEvent<X>(InState = Y)]`) is on the table but not shipped.

---

## Timeouts

Schedule a timeout from a handler:

```csharp
await Schedule(context, TimeSpan.FromSeconds(30), new BuyersRemorseExpired(Data.OrderId), ct);
TransitionTo(BuyersRemorseSagaState.Holding);
```

`Schedule<TTimeout>` builds the dedup key as `saga:<id>:<timeoutTypeFullName>` and writes through the configured `IScheduledMessageStore`. The relay polls and dispatches at the due time.

When the saga calls `Complete()`, the lifecycle calls `IScheduledMessageStore.CancelByPrefixAsync("saga:<id>:")`, removing every still-pending timeout for that saga in one shot. Cancelling a saga (via an `OrderCancellationRequested` handler that calls `Complete()`) leaves no orphaned timeouts firing later.

If a timeout *does* fire for a saga that no longer exists (e.g. a delayed dispatch the lifecycle didn't catch), the wrapper routes the message to `IHandleSagaNotFound<TTimeout>` — see below.

---

## Lifecycle: `Complete()`

```csharp
Complete();
```

That call alone:
1. Sets `Data.IsCompleted = true` and `Data.CompletedAt = utcNow`.
2. The wrapper detects the flag after `Handle` returns.
3. The wrapper calls `_db.Remove(Data)` (default policy: delete; configurable to keep for audit later).
4. The wrapper resolves `IScheduledMessageStore` from the scope and calls `CancelByPrefixAsync` to drop any pending timeouts.
5. `_db.SaveChangesAsync` commits the deletion + cancellations + any state changes the handler made — all in one transaction.

You can call `Complete()` from any handler at any point; control returns from `Handle` normally. Multiple `Complete()` calls are idempotent.

---

## Saga-not-found

When an `IHandles<TMessage>` arrives but no saga state exists (it completed already, or the message arrived before its starter), the wrapper routes to `IHandleSagaNotFound<TMessage>`:

```csharp
public sealed class OrphanedTimeoutAuditor(ILogger<OrphanedTimeoutAuditor> logger)
    : IHandleSagaNotFound<BuyersRemorseExpired>
{
    public ValueTask OnNotFoundAsync(BuyersRemorseExpired message, ICompositionContext context, CancellationToken ct)
    {
        logger.LogWarning("ORPHAN BuyersRemorseExpired for unknown order {OrderId}.", message.OrderId);
        return default;
    }
}
```

Register as scoped: `services.AddScoped<IHandleSagaNotFound<BuyersRemorseExpired>, OrphanedTimeoutAuditor>();`

Without a registered handler, the wrapper logs at debug level and discards. This is the right default — most "saga not found" arrivals are expected (cancellations, completed sagas) and don't merit a warning.

---

## DI + persistence

The saga itself, the data type, and the per-message `IEventHandler<TMessage>` wrappers are auto-registered by the generated `AddMosaic()`. The wrapper resolves the saga and the `DbContext` (detected via the saga's primary constructor — exactly one parameter inheriting `Microsoft.EntityFrameworkCore.DbContext`) from the active scope.

You're responsible for:
- Adding the `TData` entity to the model (a `DbSet<MyData>` on the DbContext is the simplest way; or `modelBuilder.Entity<MyData>()` in `OnModelCreating`).
- Wiring scheduling (`UseEFCoreScheduling<MyDbContext>()` on the `IMosaicBuilder`) if any saga calls `Schedule<T>`.

---

## Diagnostics

The source generator emits diagnostics for misuse at compile time:

| Id | Trigger |
|---|---|
| `MOSAIC_SAGA_001` | Saga has no `IStartedBy<>` marker — unreachable. |
| `MOSAIC_SAGA_002` | Marker declared (e.g. `IHandles<X>`) but no matching `Handle(X, ICompositionContext, CancellationToken)` method. |
| `MOSAIC_SAGA_003` | Message lacks the correlation property and there's no `CorrelateBy` partial. |
| `MOSAIC_SAGA_004` | `TData` doesn't inherit `SagaData`. |
| `MOSAIC_SAGA_005` | Primary constructor doesn't have a `DbContext`-derived parameter. |

---

## When to use `Saga<TData>` vs `SagaProcessor<TState>`

**Use `Saga<TData>` when:**
- The workflow advances in response to events (the common case).
- Time-based progression is modelled as a scheduled message that the saga handles when it fires.

**Use `SagaProcessor<TState>` when:**
- The workflow needs continuous polling — e.g. a watchdog that scans for stuck rows on a cadence independent of incoming messages.
- You're integrating with a system that exposes only "give me everything currently in state X" semantics.

Both are in this package. They don't compete — different shapes for different problems. If in doubt, start with `Saga<TData>`.
