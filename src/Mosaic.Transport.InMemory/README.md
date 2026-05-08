# Mosaic.Transport.InMemory

In-memory cross-container transport for [Mosaic](https://github.com/pikedev/mosaic). Routes events between DI containers in the same process via a static, named channel — built for integration tests and same-host topologies.

```csharp
var publisher = new ServiceCollection()
    .AddMosaic().UseInMemoryTransport("xc-test").Services
    .BuildServiceProvider();

var subscriber = new ServiceCollection()
    .AddMosaic().UseInMemoryTransport("xc-test").Services
    .BuildServiceProvider();

// Publish from one container; the other receives via the in-memory transport.
await publisher.GetRequiredService<ICompositionEngine>()
    .Publish(new OrderPlaced(...));
```

Containers that share a channel name see each other's publishes. Loopback suppression is by per-container sender id — a publisher never receives its own events back. Inbound dispatch goes through the same `InboundDispatch.TryDispatchAsync` resilience helper (retry + DLQ) as real transports, so the same recoverability policy applies.

Use this for:
- Integration tests that exercise the cross-process publish path without standing up real infrastructure.
- Same-host topologies where multiple modules share a process but want decoupled message flow.

For tests that focus on a single container's engine traffic (recording sends/publishes/scheduled messages), see `Mosaic.Testing` instead.
