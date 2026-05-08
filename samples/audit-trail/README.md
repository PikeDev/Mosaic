# Audit trail — the correlation graph

A single-file sample showing how Mosaic stamps every message with three identifiers — `MessageId`, `CorrelationId`, `CausationId` — and how an opt-in audit store turns that metadata into a queryable directed graph of cause and effect.

The chain: a single `OrderPlaced` is published; its handler publishes `OrderAccepted`; that handler in turn publishes `ShipmentArranged`. Three messages, three different types, one logical flow. The audit store captures every hop and the program prints them grouped by correlation.

---

## What's demonstrated

- **`MessageHeaders`** on every message — `MessageId` (unique per instance), `CorrelationId` (shared across the whole flow), `CausationId` (the parent message's id), `SentAtUtc`.
- **The engine stitches the chain.** When a handler calls `context.Publish(...)`, the new message inherits the parent's `CorrelationId` and points its `CausationId` at the parent's `MessageId`. Top-level publishes start a fresh chain (`CausationId == null`).
- **`UseInMemoryAuditing()`** wires `InMemoryMessageAuditStore` as the `IMessageAuditStore`. The engine's outbound publish path writes a `Sent` entry per message; the inbound dispatch path writes a `Received` entry. (In this single-process sample with the in-process transport, only `Sent` entries appear.)
- **`audit.ByCorrelation(corrId)`** walks the chain in time order — the canonical "what did this flow do?" query.

---

## Running it

```
dotnet run --project samples/audit-trail
```

Expected output (ids will differ each run):

```
══════ Driving the chain: OrderPlaced ══════

══════ Audit trail (grouped by CorrelationId) ══════

correlation: dc5e902d...
  Sent  Mosaic.Sample.AuditTrail.OrderPlaced         msg=18eb56aa...  cause=(root)
    Sent  Mosaic.Sample.AuditTrail.OrderAccepted     msg=d32ebd8a...  cause=18eb56aa...
      Sent  Mosaic.Sample.AuditTrail.ShipmentArranged  msg=816b0918...  cause=d32ebd8a...
```

Three things to notice:

1. **One correlation id covers all three messages.** The flow has one identity end-to-end — that's what makes it queryable.
2. **The first entry has `cause=(root)`.** It started the chain.
3. **Each child's `cause` matches its parent's `msg`.** The program walks `CausationId` pointers root-first to render the indented tree; the underlying audit log is captured leaf-first under buffered events (the deepest cascaded publish completes before its parent's terminal returns), so this re-render is what an operator actually wants to read.

---

## Why this shape

### Async systems are opaque without identifiers

In a synchronous codebase a stack trace is the chain — when something goes wrong, the call chain tells you exactly what happened. In an async codebase nothing is connected by stack: handler X publishes an event, the handler runs in a different scope, possibly a different process, often hours later when a scheduled timeout fires.

If messages don't carry identifiers, debugging a misbehaving flow means reading every log entry from every service in roughly the right time window and trying to reconstruct what called what. That doesn't scale beyond toy systems.

The fix is small but load-bearing: every message carries an id; every cascaded message points back at its parent. Now the "stack trace" of an async flow is just `SELECT * FROM audit WHERE correlation_id = X ORDER BY timestamp` — a single query, no log forensics.

### CorrelationId vs CausationId — they answer different questions

- `CorrelationId` answers *"what flow does this belong to?"*. It stays constant from the original triggering message all the way through the deepest cascaded event.
- `CausationId` answers *"what specifically caused this message?"*. It changes at every hop and forms a chain of parent-pointers back to the root.

You can't substitute one for the other. CorrelationId alone gives you a flat list of related messages but loses ordering and parentage. CausationId alone gives you a parent pointer but no way to find the rest of the flow without walking the chain. Both together make the audit log a real graph.

### Auditing is opt-in by default — nothing happens when you don't ask

`AddMosaic()` registers a no-op `IMessageAuditStore` so the engine's audit hooks are free virtual calls. `UseInMemoryAuditing()` (this sample) or `UseAuditing<TStore>()` (production) replaces it with one that actually writes. This way auditing has zero cost when you don't want it and is one chained call away when you do.

### The store interface stays simple so adapters are obvious

`IMessageAuditStore` has one method: `WriteAsync(MessageAuditEntry)`. That's it. The entry carries timestamp, message type, direction, and headers — all the framework knows about a hop. What you do with the entry is your concern: append to a queue, ship to a SIEM, batch-insert into a partition table, throw on the floor for tests. The framework doesn't constrain the persistence shape because deployments differ wildly on what they want from an audit trail.

### Production extends this — it's not just a debugging tool

The same audit log feeds compliance trails (every payment must be traceable), incident triage (what happened in this customer's flow yesterday at 14:32?), capacity planning (which event types dominate volume?), and replay (re-run the chain from a specific message). All of it falls out of the same per-message identifiers; you don't add separate observability machinery for each use case.
