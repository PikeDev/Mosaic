# Mosaic.Transport.Postgres

Cross-process event delivery over Postgres `LISTEN/NOTIFY`. Events published with `engine.Publish(event)` reach handlers in other processes through this transport, with retries, dead-lettering, and per-process loopback suppression.

Pair with `Mosaic.Outbox.EFCore` (or `Mosaic.Outbox.Postgres`) for at-least-once *publishing*. This package alone provides best-effort delivery + reliable inbound handling; it does not persist outbound events on its own.

---

## Quick start

```csharp
services.AddMosaic()
    .UsePostgresTransport(connectionString);
```

That's it. `AddMosaic()` auto-registers the in-process dispatch path; `UsePostgresTransport` swaps the default no-op `IEventTransport` for `PostgresEventTransport` and starts a hosted listener on the `mosaic_events` channel.

---

## What it does

### Outbound (`PublishAsync`)

When `engine.Publish(event)` runs and the local in-process fan-out completes, the engine calls `IEventTransport.PublishAsync(typeFullName, jsonPayload, ct)`. `PostgresEventTransport`:

1. Wraps the payload in an envelope: `{ Sender, Type, PayloadBase64, MessageId? }`.
2. Calls `pg_notify('mosaic_events', envelopeJson)`.
3. Returns. No durability — if no listener is connected, the notification is dropped.

For durable outbound, replace this transport's role with `Mosaic.Outbox.EFCore` (`UseEFCoreOutbox<TDb>()` swaps `IEventTransport` to `OutboxRoutingTransport`, which writes to a table; the outbox relay calls *this* package's `pg_notify` shape later).

### Inbound (the listener)

`PostgresEventTransport` is also an `IHostedService`. On startup it opens a connection, runs `LISTEN mosaic_events`, and waits for notifications via `NpgsqlConnection.WaitAsync`. On each notification:

1. Parse the envelope.
2. **Loopback suppression** — if `envelope.Sender == _senderId` (this process), drop. Each process generates a fresh `_senderId` GUID at startup, so a publisher never sees its own NOTIFY come back.
3. Decode the base64 payload.
4. Hand off to `InboundDispatch.TryDispatchAsync` on the threadpool.

`TryDispatchAsync` (in `Mosaic.Runtime`) is the shared resilience helper:

- Creates a fresh DI scope.
- Optional **inbox dedup**: if `envelope.MessageId` is non-null and an `IInboxStore` is registered, calls `WasConsumedAsync(messageId)` and skips the dispatch on a hit.
- Resolves `IInboundEventDispatcher` (the source-generated engine, which implements both `ICompositionEngine` and `IInboundEventDispatcher`) and calls `DispatchInboundAsync(typeFullName, payload, messageId, ct)`.
- On exception, retries with backoff up to `MosaicResilienceOptions.MaxAttempts` (3 by default).
- On exhaustion, writes the envelope to `IDeadLetterStore` — `PostgresDeadLetterStore` by default for this package, which auto-creates a `mosaic_dead_letters` table and inserts: `{ Id, Type, Payload, Error, FailedAt }`.

### Envelope

```
{ "Sender": "<senderId>", "Type": "<TypeFullName>", "PayloadBase64": "<base64>", "MessageId": "<guid?>" }
```

Carried in the `pg_notify` payload (capped at ~8000 bytes by Postgres). `MessageId` is only set when the publish came through an outbox relay — direct `PublishAsync` calls have `null`, in which case inbox dedup is bypassed.

---

## What `UsePostgresTransport(connStr)` registers

| Service | Lifetime | Notes |
| --- | --- | --- |
| `NpgsqlDataSource` | Singleton | Created from the connection string. |
| `PostgresEventTransport` | Singleton | Concrete type. |
| `IEventTransport` | Singleton | Resolves to the same `PostgresEventTransport`. |
| `IHostedService` | Singleton | Same instance — runs the LISTEN loop. |
| `IDeadLetterStore` | Singleton | Replaces `Mosaic.Runtime`'s `InMemoryDeadLetterStore` with `PostgresDeadLetterStore`. Table auto-created on first dead-letter write. |

If you also call `UseEFCoreOutbox<TDb>()`, that call removes the `IEventTransport` registration above and replaces it with `OutboxRoutingTransport`. The `IHostedService` listener stays — so the process still receives inbound notifications, but outbound `Publish` now goes through the outbox table instead of straight to `pg_notify`. The outbox relay then ships rows from the table via `pg_notify`, using this transport's envelope shape.

---

## What it does NOT guarantee

- **Durability of direct publishes.** A `pg_notify` with no listener is silently dropped. Pair with an outbox if you need at-least-once publishing.
- **Ordering across publishers.** Postgres `NOTIFY` delivers per channel in receive order, but two publishers can interleave. If you need per-key ordering, use a saga or a topic-per-key scheme.
- **Cross-database delivery.** Transport is bounded to one Postgres database. Multi-DB topologies need bridging.
- **Payload size.** `pg_notify` caps at ~8000 bytes for the channel + payload combined. The framework does not chunk; over-sized events fail at publish. If your events approach this limit, consider storing the body elsewhere and shipping a reference.

---

## Tuning + observability

`MosaicResilienceOptions` (registered by `AddMosaic()`) controls retry behaviour:

```csharp
services.Configure<MosaicResilienceOptions>(o =>
{
    o.MaxAttempts = 5;
    // o.RetryDelayFor(attempt) — override for custom backoff.
});
```

Logs:
- `Information` on listener start (`PostgresEventTransport listening on 'mosaic_events' (sender=…)`).
- `Warning` on retry attempts.
- `Error` on dispatch exhaustion (followed by a dead-letter write).
- `Error` on listener IO failure (loop retries every 1s).

Telemetry: dispatch goes through the source-generated engine, which emits OpenTelemetry spans on the `Mosaic` activity source — enabling that source captures every inbound dispatch automatically.

---

## Testing

For tests, replace this package with `Mosaic.Transport.InMemory` (`UseInMemoryTransport()`). Same wire shape, same listener semantics, no Postgres needed.
