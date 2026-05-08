# Mosaic

A typed, source-generated .NET framework for **mediator-style dispatch, view-model composition, and message-driven workflows**. Each handler contributes a tile; the framework assembles the picture.

## Why Mosaic exists

A mediator is the right tool for in-process dispatch — keeping send-sites ignorant of the handler that runs and giving you one place to add cross-cutting behavior. But classic mediators stop at single-handler request/response. They don't help when **the response itself is composed from data owned by several services**, and they don't address what shows up the moment a system stops being a single request thread.

The first gap — *several services each contribute their slice of one view-model, in parallel, with no central orchestrator* — is what falls out of decomposing a system along business-capability lines. The product page on a webshop isn't owned by one service; it's product info from catalog, price from marketing, stock from inventory. Each service writes the field it owns; nobody owns the page. The pattern is in the architectural literature under "view-model composition", and Mosaic implements it as a first-class concept (`IComposable<TVm>` + many `IComposer<,TVm>`s) sitting alongside the more familiar request/response (`IRequest<T>` + one `IRequestHandler<,>`) and notification (`IEvent` + many `IEventHandler<>`s) shapes.

The second gap — *workflows that span hours or days, state changes that must commit together with the events they raise, messages that survive process restarts* — is what every real distributed system needs once async and time enter the picture. Mosaic implements these as first-class concepts too: `Saga<TData>` for time-spanning processes, an atomic outbox so `Publish` commits in the same transaction as state, scheduled messages for self-timeouts, a correlation graph that stitches every hop into one queryable chain, per-exception recoverability, and a critical-error hook for operations.

So: a mediator, a composer, and a message-driven framework. All source-generated; all invokable from anywhere with an `IServiceProvider` — controllers, background jobs, message handlers, gRPC services.

## What it does

- **Send** — a typed `IRequest<TResponse>` is dispatched to exactly one `IRequestHandler<,>`.
- **Compose** — a typed `IComposable<TViewModel>` is dispatched to **every** `IComposer<,>` registered for that VM; each composer writes the slice of the VM its service owns, all in parallel against the same instance.
- **Publish** — a typed `IEvent` is fanned out to many `IEventHandler<>` implementations, with buffered (outbox-style) or eager publishing.

The dispatcher is **source-generated** in your composition root — no reflection at startup, no virtual calls on the hot path, AOT/trim-friendly. Handler discovery scans the consumer's compilation plus every transitively-referenced assembly that depends on `Mosaic.Abstractions`; the framework projects never reference handler projects.

Handler lifetimes are honored — Singleton, Scoped, and Transient all work because handlers are resolved per invocation from the active `IServiceProvider`.

## Packages

The core is a meta package + abstractions + source generator. Everything else is opt-in.

**Core**

| Package | Purpose |
| --- | --- |
| `Mosaic.Abstractions` | Interfaces (`IRequest`, `IComposable`, `IEvent`, handlers, behaviors, engine, context, transport seams, scheduled-message store, inbox/outbox seams, audit + recoverability + critical-error seams). Reference in every project that defines messages or handlers. |
| `Mosaic.SourceGenerator` | Roslyn `IIncrementalGenerator` that emits the dispatcher, DI registrations, and saga wrappers. Reference **only** in the composition root, with `PrivateAssets="all"`. |
| `Mosaic.Runtime` | Internal helpers shared by generated code (composition context, telemetry, retry + DLQ, pooled buffer writer). |
| `Mosaic` | Meta package — pulls in Abstractions, Runtime, and SourceGenerator. Reference this in your composition root unless you need finer control. |

**Cross-process delivery (pick a transport)**

| Package | Purpose |
| --- | --- |
| `Mosaic.Transport.Postgres` | `UsePostgresTransport(connStr)` — Postgres `LISTEN/NOTIFY` for cross-process events. Sender-id loopback suppression, retry + Postgres-backed DLQ on inbound. Pair with an outbox for at-least-once publishing. See `src/Mosaic.Transport.Postgres/README.md`. |
| `Mosaic.Transport.InMemory` | In-memory transport for tests + same-host topologies. |

**Atomic outbox + scheduling + inbox dedup**

| Package | Purpose |
| --- | --- |
| `Mosaic.Outbox.EFCore` | `UseEFCoreOutbox<TDb>()` (atomic outbox: `Publish` writes a row into the consumer's DbContext change tracker — commits with state in one transaction); `UseEFCoreScheduling<TDb>()` (scheduled messages, including saga timeouts); inbox dedup keyed on `MessageId` so duplicate transport deliveries no-op. See `src/Mosaic.Outbox.EFCore/README.md`. |
| `Mosaic.Outbox.Postgres` | Standalone Postgres outbox table (no EF dependency). For consumers that don't use EF Core for state. |

**Sagas**

| Package | Purpose |
| --- | --- |
| `Mosaic.Sagas` | `Saga<TData>` — message-driven sagas declared via marker interfaces; the generator emits per-message wrappers. `[Correlation]`, `[During(state)]`, scheduled timeouts via `Schedule<T>`, lifecycle via `Complete()`, opt-in `IHandleSagaNotFound<T>`. Also ships `SagaProcessor<TState>` for the rarer time-driven shape. See `src/Mosaic.Sagas/README.md`. |
| `Mosaic.Sagas.EFCore` | EF Core implementation of `ISagaStateStore<TData>` — auto-registered for any saga whose primary constructor takes a `DbContext`. Sagas with custom backends register their own `ISagaStateStore<TData>` and skip this package. |

**Serialization**

| Package | Purpose |
| --- | --- |
| (built into `Mosaic.Runtime`) | Default `IMosaicSerializerRegistry` is `System.Text.Json`. The serializer interface (`IMosaicSerialize<in T>` / `IMosaicDeserialize<out T>`) is split for variance and accepts `IBufferWriter<byte>` / `ReadOnlySequence<byte>`, so serializers can avoid allocations on the publish path. |
| `Mosaic.Serialization.MemoryPack` | MemoryPack adapter — register `MemoryPackMosaicSerializerRegistry` before `AddMosaic()` to swap the JSON default for binary MemoryPack on the wire. Works per-type provided the type is declared `[MemoryPackable] partial`. |

**Test harness**

| Package | Purpose |
| --- | --- |
| `Mosaic.Testing` | `MosaicTestHarness` and `AddMosaicTestHarness()` — captures sends, publishes, and scheduled messages so tests assert on what flowed through the engine without standing up a transport. |

## Getting started

### Send — one request, one handler

```csharp
// 1. Define a message (in the project that owns it).
public sealed record GetGreeting(string Name) : IRequest<string>;

// 2. Implement the handler (in the same project, or anywhere downstream).
public sealed class GreetingHandler : IRequestHandler<GetGreeting, string>
{
    public ValueTask<string> Handle(GetGreeting r, ICompositionContext ctx, CancellationToken ct)
        => new($"Hello, {r.Name}");
}

// 3. In the composition root: register, then dispatch.
var services = new ServiceCollection();
services.AddMosaic();                       // generated extension; discovers every handler
var sp = services.BuildServiceProvider();

var engine = sp.GetRequiredService<ICompositionEngine>();
var greeting = await engine.Send(new GetGreeting("world"));
```

### Compose — one request, many composers contributing slices of one view-model

```csharp
// 1. The view-model lives at the edge — no service "owns" it as a whole, only its slices.
public sealed class ProductTileVm
{
    public int ProductId { get; set; }
    public string Title { get; set; } = "";
    public decimal UnitPrice { get; set; }
}

// 2. The composable carries identifiers only; each composer fetches its own data.
public sealed record GetProductTile(int ProductId) : IComposable<ProductTileVm>;

// 3. Each contributing service implements IComposer<,> and writes ONLY the field it owns.
//    In a real codebase these classes would live in different assemblies (Catalog, Pricing, …);
//    Mosaic finds them across every transitively-referenced assembly.
public sealed class CatalogTileComposer : IComposer<GetProductTile, ProductTileVm>
{
    public ValueTask Compose(GetProductTile r, ProductTileVm vm, ICompositionContext ctx, CancellationToken ct)
    {
        vm.ProductId = r.ProductId;
        vm.Title     = $"Product #{r.ProductId}";   // would come from a catalog store
        return default;
    }
}

public sealed class PricingTileComposer : IComposer<GetProductTile, ProductTileVm>
{
    public ValueTask Compose(GetProductTile r, ProductTileVm vm, ICompositionContext ctx, CancellationToken ct)
    {
        vm.UnitPrice = 19.99m;                       // would come from a pricing store
        return default;
    }
}

// 4. Dispatch — the engine runs both composers in parallel against the same VM instance.
var tile = await engine.Compose(new GetProductTile(42));
// tile.Title == "Product #42", tile.UnitPrice == 19.99
```

Three things to notice:

1. The orchestrator is the engine itself — there is no `ProductTileService` that calls Catalog and Pricing in turn. `engine.Compose` discovers every composer registered for `ProductTileVm` and runs them in parallel via `Task.WhenAll`.
2. Each composer writes a different field, so the parallel writes can't conflict — no locks needed.
3. Adding a third contributor (e.g. an `InventoryTileComposer` that fills `InStock`) is a one-class change in a new assembly. Nothing existing changes.

For a fuller walkthrough — including the command/event side, nested composables, and *why* each project boundary is drawn where it is — see the **runnable end-to-end sample** at [`samples/`](samples/) and [`samples/README.md`](samples/README.md).

## Beyond send/compose/publish

The three core verbs are the headline. Most real systems also need durability, orchestration, and operational hooks; those are opt-in, configured via builder methods that chain off `AddMosaic()`.

**Atomic outbox.** `Publish` in a handler normally writes through the configured `IEventTransport`. With `UseEFCoreOutbox<TDbContext>()` wired up, it writes a row into the consumer's `DbContext` change tracker instead — so the outbound event commits in the same SQL transaction as the state change that produced it. A relay polls the table and ships rows via `LISTEN/NOTIFY` (or whatever transport is configured) afterwards. No "saved state but lost the event" gap. Details in [`src/Mosaic.Outbox.EFCore/README.md`](src/Mosaic.Outbox.EFCore/README.md).

**Inbox dedup.** Outbox + transport gives at-least-once delivery; inbox dedup turns it into at-most-once *side effects*. The wire envelope carries a `MessageId` (the outbox row's id, stable across redeliveries); on inbound, the framework checks an inbox table and skips the dispatch if seen. Wired up automatically by `UseEFCoreOutbox<TDb>`.

**Scheduled messages.** `context.ScheduleMessage(delay, message, dedupKey)` persists a message to be dispatched later — same envelope shape as outbound, same `IInboundEventDispatcher` on arrival. `context.CancelScheduledMessage(dedupKey)` removes one before it fires. Backed by `UseEFCoreScheduling<TDbContext>()`. The saga base class wraps this as `Schedule<TTimeout>(...)` for self-scheduled timeouts.

**Sagas.** `Saga<TData>` — message-driven sagas declared via marker interfaces (`IStartedBy<TMessage>`, `IHandles<TMessage>`). The source generator reads the markers, the `[Correlation]` property, and any `[During(state)]` guards, and emits per-message `IEventHandler<TMessage>` wrappers that load the saga state, run the user's `Handle`, and persist atomically. Saga lifecycle (`Complete()`) auto-cancels pending timeouts. Full design in [`src/Mosaic.Sagas/README.md`](src/Mosaic.Sagas/README.md).

**Correlation graph + auditing.** Every message that flows through the engine carries a `MessageHeaders` value: `MessageId`, `CorrelationId`, `CausationId`, `SentAtUtc`. The engine stitches these together so a single async flow shares one `CorrelationId` across every hop, and each hop's `CausationId` points back at the message that triggered it. Plug in an `IMessageAuditStore` via `UseAuditing<TStore>()` (or `UseInMemoryAuditing()` for tests) to capture every Sent/Received hop — query by `CorrelationId` to walk a saga's full lifecycle.

**Recoverability policy.** Replace the default "retry then DLQ" curve with per-exception rules via `UseRecoverability(ctx => ...)`. Return `RecoverabilityAction.Retry(delay)` for transient failures (gateway timeouts, deadlock retries), `RecoverabilityAction.DeadLetter` for poison messages that won't get better, `RecoverabilityAction.Discard` for fire-and-forget. The policy decides per failed attempt; the dispatch loop obeys.

**Critical errors.** `UseCriticalErrorHandler<THandler>()` registers an escalation hook for catastrophic conditions the framework can detect but not recover from — e.g. the dead-letter store itself failing. The default handler logs at `Critical`; real deployments wire it to PagerDuty / Slack / endpoint shutdown. The handler receives the exception aggregate (original failure + DLQ failure) and the message's correlation graph for triage.

**Pluggable serialization.** The default registry is `System.Text.Json` (reflection-based for zero-config). Register your own `IMosaicSerializerRegistry` before `AddMosaic()` to swap in a different format — `Mosaic.Serialization.MemoryPack` ships a binary MemoryPack adapter for hot paths where JSON parsing is the bottleneck. Each `IMosaicSerializer<T>` writes to `IBufferWriter<byte>` and reads from `ReadOnlySequence<byte>`, and the engine rents from a pooled writer (`MosaicBufferWriter`), so the publish path doesn't allocate per message.

**AOT-friendly JSON.** For native AOT or trimmed publish, swap the reflection default for a source-generated `JsonSerializerContext`. Decorate the partial with `[MosaicJsonContext]` to opt into the coverage analyzer (MOSAIC0006), which warns at compile time when an event is missing from the context:

```csharp
[MosaicJsonContext]
[JsonSerializable(typeof(OrderPlaced))]
[JsonSerializable(typeof(OrderAccepted))]
internal sealed partial class WebshopJsonContext : JsonSerializerContext;

services.AddMosaic()
    .UseSystemTextJsonContext(WebshopJsonContext.Default)
    .UsePostgresTransport(connStr);
```

The registry resolves each event's `JsonTypeInfo<T>` from the context — no reflection at runtime, AOT-clean. The analyzer keeps the context in sync with the event catalog as you add events; if you forget the analyzer, the runtime registry still throws on first use with a message pointing at the fix.

These layers compose. A saga handler can `Publish` events (atomic via outbox), `Schedule` self-timeouts (atomic via scheduling), and call `Complete` (state delete + timeout cleanup, all in one save) — and every hop ends up in the audit trail under one `CorrelationId`. The webshop sample exercises every layer end-to-end.

## Layout

```
mosaic/
  src/
    Mosaic.Abstractions/            Interfaces, attributes, exceptions
    Mosaic.SourceGenerator/         Roslyn IIncrementalGenerator
    Mosaic.Runtime/                 Internal helpers used by generated code
    Mosaic/                         Meta package
    Mosaic.Sagas/                   Saga<TData> + SagaProcessor<TState>
    Mosaic.Sagas.EFCore/            EF Core saga state store
    Mosaic.Transport.Postgres/      LISTEN/NOTIFY transport + DLQ
    Mosaic.Transport.InMemory/      In-memory transport for tests
    Mosaic.Outbox.EFCore/           Atomic outbox + scheduling + inbox dedup
    Mosaic.Outbox.Postgres/         Standalone Postgres outbox (no EF dep)
    Mosaic.Serialization.MemoryPack/  MemoryPack serializer adapter
    Mosaic.Testing/                 Test harness for asserting on engine traffic
  samples/                          Runnable demos — see samples/README.md
    decomposed-webshop/             Send + Compose + Publish across multiple service assemblies
    saga-timeout/                   Saga schedules a self-timeout, absorbs a competing cancel
    audit-trail/                    Correlation graph captured + rendered root-first
  tests/
    Mosaic.Tests/                   Runtime / dispatch tests
    Mosaic.SourceGenerator.Tests/   Snapshot tests for the generator
```

## Building

```
dotnet build
dotnet test
dotnet run --project samples/decomposed-webshop/Mosaic.Sample.Host
dotnet run --project samples/saga-timeout
dotnet run --project samples/audit-trail
```

## License

MIT. See `LICENSE`.
