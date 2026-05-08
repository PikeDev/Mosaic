# Mosaic.Testing

Test harness for Mosaic. Records every `Send`, `Compose`, and `Publish` that flows through the engine, exposes them as typed assertion collections, and ships ergonomic `WaitForAsync` helpers for async-arrival scenarios.

Backed by the open-generic publish/compose/pipeline behaviors — the recording is a thin wrapper that participates in the existing chain, not a parallel observation mechanism. So everything routed through the engine (whether issued by your test code, a request handler, an event handler, or a saga's `Schedule` callback) shows up in the recording with no special wiring.

---

## Quick start

```csharp
using Mosaic;
using Mosaic.Testing;

// One-time per test assembly: list the recording behaviors in your CompositionConfiguration
// alongside any other behaviors you have. They degrade to no-ops when no MosaicTestRecorder is
// registered, so they're safe to keep wired permanently — production runs that don't add the
// recorder pay nothing.
[assembly: CompositionConfiguration(
    PipelineBehaviors = new[] { typeof(RecordingPipelineBehavior<,>) },
    PublishBehaviors  = new[] { typeof(RecordingPublishBehavior<>) },
    ComposeBehaviors  = new[] { typeof(RecordingComposeBehavior<,>) })]

public class OrderTests
{
    [Fact]
    public async Task Placing_an_order_publishes_OrderAccepted()
    {
        await using var harness = await MosaicTestHarness.CreateAsync(s =>
        {
            s.AddMosaicTestHarness();   // registers the recorder
            s.AddSales();               // your service's registration extension
            s.AddMosaic();              // generated extension; discovers handlers
        });

        await harness.Engine.Send(new PlaceOrder(orderId: 42));

        var accepted = await harness.Published<OrderAccepted>().WaitForAsync(count: 1);
        accepted[0].OrderId.ShouldBe(42);
    }
}
```

That's the whole picture. Two attribute lines per assembly + one `AddMosaicTestHarness()` per test wiring + the harness itself.

---

## What the harness gives you

| Member | Returns |
| --- | --- |
| `harness.Engine` | The `ICompositionEngine` — call `Send`, `Compose`, `Publish` directly. |
| `harness.Services` | The full `IServiceProvider`. Resolve anything else you wired up. |
| `harness.Sent<TRequest>()` | Recorded request instances of type `TRequest`. |
| `harness.Published<TEvent>()` | Recorded event instances of type `TEvent`. |
| `harness.Composed<TRequest>()` | The composable requests passed in (input snapshots). |
| `harness.ComposedResults<TViewModel>()` | The populated view-models after the compose chain ran. |
| `harness.Recorder` | The underlying `MosaicTestRecorder` if you need lower-level access. |

Each of those returns a `RecordedMessages<T>`:

```csharp
RecordedMessages<TileVm> tiles = harness.ComposedResults<TileVm>();
tiles.Count;                                            // sync count
tiles.All;                                              // snapshot, oldest first
tiles.Of<SomeSubtype>();                                // filtered by subtype

await tiles.WaitForAsync(count: 3, TimeSpan.FromSeconds(2));
await tiles.WaitForAsync(t => t.UnitPrice > 0, TimeSpan.FromSeconds(2));

tiles.Clear();   // reset between phases of a long test
```

`WaitForAsync` polls every 50ms with a default 5s timeout. It throws `TimeoutException` on miss — wrap with try/catch only if you genuinely expect non-arrival.

---

## How the recording works

Each recording behavior is registered as an open generic in DI; the source generator closes them per `(TRequest, TResponse)` / `TEvent` / `(TRequest, TViewModel)` at compile time, the same way it does the regular pipeline behaviors. At runtime, each behavior tries to resolve a `MosaicTestRecorder` from the scope and records there if present. If no recorder is registered, the behavior is a transparent no-op.

This has three consequences worth knowing:

1. **Events without subscribers aren't recorded.** Mosaic's source generator only emits a `Publish_T` dispatch for events that have at least one `IEventHandler<T>`. Events with zero subscribers are silent no-ops at the engine level — neither handlers nor publish-behaviors run for them. If you want a published event recorded for assertion purposes but don't have a real handler, add a no-op handler. Most real systems already have at least one handler per published event, so this rarely matters in practice.

2. **Recording is per-engine-instance, not per-call.** The recorder is a singleton in the harness's container. If you run multiple harnesses in the same test (multi-host tests), each has its own recorder. Cross-process events arrive via the configured transport (e.g. `Mosaic.Transport.InMemory`) and are recorded by the receiving harness's behaviors when they fan out to handlers there.

3. **The recorder type itself is the public surface.** If you have a one-off scenario where the harness's typed methods aren't enough (e.g. you want a single recorder spanning multiple harnesses), inject `MosaicTestRecorder` directly — it's just a thread-safe collection.

---

## A worked example

```csharp
[Fact]
public async Task Compose_records_input_request_and_populated_viewmodel()
{
    await using var harness = await MosaicTestHarness.CreateAsync(s =>
    {
        s.AddMosaicTestHarness();
        s.AddMosaic();
    });

    var tile = await harness.Engine.Compose(new GetTile(productId: 7));

    // Input recorded
    harness.Composed<GetTile>().Count.ShouldBe(1);
    harness.Composed<GetTile>().All[0].ProductId.ShouldBe(7);

    // Populated VM recorded too
    var results = harness.ComposedResults<TileVm>().All;
    results.Count.ShouldBe(1);
    results[0].ProductId.ShouldBe(7);
    results[0].Title.ShouldBe("Product #7");
    results[0].UnitPrice.ShouldBe(19.99m);
}

[Fact]
public async Task Cancelling_within_window_does_not_publish_OrderAccepted()
{
    await using var harness = await MosaicTestHarness.CreateAsync(s =>
    {
        s.AddMosaicTestHarness();
        s.AddSales();
        s.AddMosaic();
    });

    var orderId = await harness.Engine.Send(new ProceedToCheckout(/* ... */));
    await harness.Engine.Send(new PlaceOrder(orderId));
    await harness.Engine.Send(new CancelOrder(orderId, "changed_mind", /* ... */));

    // Wait past the buyers-remorse window
    await Task.Delay(TimeSpan.FromSeconds(5));

    harness.Published<OrderAccepted>().Count.ShouldBe(0);
}
```

---

## Lifecycle

`MosaicTestHarness.CreateAsync` builds an `IHost`, calls `StartAsync`, returns the harness. `await using` (or explicit `await harness.DisposeAsync()`) stops the host cleanly — saga relays, schedule pollers, transport listeners all shut down before the test exits.

For long-running tests with multiple phases, `harness.Recorder.Clear()` resets every category so the next phase asserts on fresh data.

---

## Caveats

- The harness uses `Microsoft.Extensions.Hosting`'s `Host.CreateEmptyApplicationBuilder` — empty config, no `appsettings.json`, no `IConfiguration` source. If your services need configuration, populate it inside the `configure` callback (`builder.Services.Configure<MyOptions>(...)` or wire `IConfiguration` manually).
- Tests that don't use the harness but live in the same assembly with the recording behaviors wired need no extra work — the behaviors degrade to no-ops when no `MosaicTestRecorder` is registered.
- Multi-host tests (two harnesses in one test) need their own transport. Pair with `Mosaic.Transport.InMemory` to share a static channel between them.
