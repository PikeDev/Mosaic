# Mosaic.Outbox.EFCore

Three EF Core–backed reliability layers that share one DbContext + one set of auto-managed tables:

1. **Atomic outbox** — `Publish` writes the outbound event into the consumer's `DbContext` change tracker; the next `SaveChanges` commits it in the same transaction as state. A relay polls and ships via the configured transport.
2. **Inbox dedup** — inbound envelopes carry a stable `MessageId`; the framework records consumed ids in an inbox table so duplicate transport deliveries are skipped.
3. **Scheduled messages** — `context.ScheduleMessage(delay, …)` persists a message to be dispatched later; a relay polls and dispatches at the due time. Saga timeouts ride on this.

All three opt-in independently. All three commit through the consumer's existing `DbContext`, so atomicity with state changes is the default.

---

## Quick start

Two calls in your composition root, one call on your `DbContext` registration. That's it.

```csharp
// 1. On the DbContext: register the entities and suppress EF's "model has changes" warning
//    (the relays auto-create the tables via CREATE TABLE IF NOT EXISTS, no migration needed).
services.AddDbContext<BillingDbContext>(opts =>
    opts.UseNpgsql(connStr, b => b.MigrationsHistoryTable("__EFMigrationsHistory", BillingDbContext.Schema))
        .UseMosaicOutbox<BillingDbContext>()
);

// 2. On the Mosaic builder: pick which layers to wire up.
services.AddMosaic()
    .UsePostgresTransport(connStr)             // cross-process LISTEN/NOTIFY
    .UseEFCoreOutbox<BillingDbContext>()       // outbox + inbox dedup
    .UseEFCoreScheduling<BillingDbContext>();  // scheduled messages
```

The DbContext does **not** need a `DbSet<MosaicOutboxEntry>` (or inbox/scheduled). Doesn't need an `IMosaicOutboxDbContext` interface either. `UseMosaicOutbox<T>()` registers the entities via an `IModelCustomizer`. Tables auto-create at first relay tick.

---

## What each layer does

### `UseEFCoreOutbox<TDbContext>()`

Atomic outbox. Replaces the engine's `IEventTransport` with `OutboxRoutingTransport`, which routes every `Publish` through `IOutboxBuffer.Enqueue` — implemented by `EFCoreOutboxBuffer<TDb>`, which calls `db.Add(new MosaicOutboxEntry { … })` on the consumer's DbContext. The next `SaveChangesAsync` commits the row in the same transaction as state changes.

Adds:
- `IOutboxBuffer` (scoped) — `EFCoreOutboxBuffer<TDb>`, with a dispose-time fallback save for the rare *Save → Publish → return* path (commits the outbox row in a separate transaction; logs a debug line so the non-atomic path is observable).
- `IInboxStore` (scoped) — `EFCoreInboxStore<TDb>`. `WasConsumedAsync(messageId)` queries the inbox table; `MarkConsumedAsync(messageId)` stages a row that commits with handler state.
- `IEventTransport` (scoped, replaces default) — `OutboxRoutingTransport`.
- `EFCoreOutboxRelayHostedService<TDb>` — polls `mosaic_outbox` rows where `SentAt IS NULL`, ships them via `pg_notify` on the `mosaic_events` channel using the same envelope shape as `PostgresEventTransport`. The outbox row's `Id` becomes the envelope's `MessageId` so the receiving inbox can dedup. Auto-creates the `mosaic_outbox` and `mosaic_inbox` tables on first poll.

Tables (auto-created in your DbContext's default schema):

```
mosaic_outbox  (Id, Sender, TypeFullName, Payload, QueuedAt, SentAt nullable)
mosaic_inbox   (MessageId PK, ConsumedAt)
```

### `UseEFCoreScheduling<TDbContext>()`

Persistent scheduled messages. `context.ScheduleMessage(delay, msg, dedupKey, ct)` — backed by `EFCoreScheduledMessageStore<TDb>`, which `db.Add`s a `MosaicScheduledEntry`. `context.CancelScheduledMessage(dedupKey)` deletes the still-pending row.

Adds:
- `IScheduledMessageStore` (scoped) — `EFCoreScheduledMessageStore<TDb>`. Also provides `CancelByPrefixAsync(prefix)`, used by the saga lifecycle to clean up timeouts when a saga calls `Complete()`.
- `EFCoreScheduledRelayHostedService<TDb>` — polls `mosaic_scheduled` for rows where `DispatchedAt IS NULL AND DueAt <= now()`, dispatches via the in-process `IInboundEventDispatcher`, stamps `DispatchedAt`. The scheduled row's `Id` is the envelope's `MessageId`, so the inbox dedups if a row ever fires twice.

Tables:

```
mosaic_scheduled (Id, DedupKey, DueAt, TypeFullName, Payload, QueuedAt, DispatchedAt nullable)
                 unique partial index on DedupKey where DispatchedAt IS NULL
                 partial index on DueAt where DispatchedAt IS NULL
```

### `UseMosaicOutbox<TDbContext>()`

The DbContext-side companion to the two above. Two effects, both required for either layer to work:

1. Registers `MosaicOutboxEntry`, `MosaicScheduledEntry`, `MosaicInboxEntry` in the model via `MosaicOutboxModelCustomizer`, an `IModelCustomizer` replacement. No `DbSet<>` declarations needed.
2. Suppresses EF's `PendingModelChangesWarning` for these entities. The relays auto-create the tables via `CREATE TABLE IF NOT EXISTS`; we don't want a consumer's `MigrateAsync()` failing because the model "has changes."

Naming is a little awkward — the call also covers scheduling and inbox — but renaming would break adopters and the alternative (`UseMosaic<T>()`) is too generic. Worth flagging if you've got a better name.

---

## Patterns that ride atomically

The most common shapes the source-gen + this package make easy:

**Handler does state + emits an event.** `db.Save → ctx.Publish → return`. The save commits state. The publish enqueues to the outbox via the routing transport, which `db.Add`s a `MosaicOutboxEntry`. There's no further save — the buffer's `IAsyncDisposable` flushes the outbox row in a fallback transaction. Not atomic with state, but no event loss.

**Handler does state + emits an event, ordered correctly.** `ctx.Publish → db.Save → return`. Publish enqueues into the change tracker first; Save commits state + outbox row in one transaction. Fully atomic. This is the order the saga base class uses internally (`Schedule<T>` → `Save` happens via the wrapper).

**Saga lifecycle.** Saga calls `Complete()`; the wrapper deletes the saga state row + cancels pending timeouts via `IScheduledMessageStore.CancelByPrefixAsync` + saves. State delete, timeout cancellations, and any other saga-state mutations all commit in one transaction.

**Inbound dedup.** Transport delivers an envelope with `MessageId = X`. `InboundDispatch.TryDispatchAsync` resolves `IInboxStore` from the scope; calls `WasConsumedAsync(X)`. If `true`, skip the dispatch. After successful dispatch, calls `MarkConsumedAsync(X)`. Atomic with handler state if the handler saves; the buffer's fallback save handles the no-save case.

---

## What it does not do

- **No transport.** This package routes events to a table; the relay then hands rows to whichever `IOutboxShipper` is registered. `Mosaic.Transport.Postgres` ships `PostgresOutboxShipper` automatically when you call `.UsePostgresTransport(...)`; alternative transports (NATS, RabbitMQ, custom) just register their own `IOutboxShipper` before `UseEFCoreOutbox<TDb>()` runs.
- **No EF migrations.** Tables auto-create via `CREATE TABLE IF NOT EXISTS` at first relay poll (Postgres-flavoured DDL). This is intentional — opting in to outbox shouldn't force consumers to generate a migration. If your team prefers explicit migrations for everything, you can add the entity configs to your own `OnModelCreating` and skip `UseMosaicOutbox`. For non-Postgres providers, manage the tables via your own migration and skip the auto-create.
- **No saga state.** Sagas use this package transitively (the saga lifecycle resolves `IScheduledMessageStore` for timeout cleanup), but saga state lives in the consumer's own DbContext on the consumer's own entity types. See `Mosaic.Sagas`.
- **No retry/DLQ on outbound shipping.** The outbox relay marks a row sent only after the shipper returns successfully. If shipping fails, the row stays un-sent and the next poll retries. Inbound retry + DLQ is in `Mosaic.Transport.Postgres`.

---

## Diagnostics + telemetry

The package logs at `Information` for relay startup, `Debug` for normal poll cycles + dedup hits, `Warning` for the dispose-time fallback save (signals a Save → Publish ordering), `Error` for failed dispatches.

Add `IHandleSagaNotFound<T>` registrations if you want to audit orphaned scheduled messages — see `Mosaic.Sagas/README.md`.

---

## Footprint

The runtime cost on the hot path: one `db.Add(MosaicOutboxEntry)` per `Publish`; one `WasConsumedAsync` query per inbound envelope (cached at the inbox table — partial index on `MessageId` makes it cheap); one `db.Add(MosaicScheduledEntry)` per `Schedule`. The relays poll every 500 ms by default (configurable via the options class on each `Use*` call).
