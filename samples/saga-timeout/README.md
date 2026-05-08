# Saga timeout — buyer's remorse

A single-file sample showing a Mosaic saga that schedules a self-timeout, absorbs a competing event during the hold window, and only emits a downstream effect if the timeout actually fires.

The saga is the canonical "buyer's remorse" pattern. Sales fires `OrderPlaced`; the saga starts and schedules `BuyersRemorseExpired` to fire after a hold window. If the customer changes their mind in time, an `OrderCancellationRequested` arrives and the saga completes silently — no `OrderAccepted`. If the hold expires, the timeout fires and the saga publishes `OrderAccepted` for downstream subscribers.

---

## What's demonstrated

- **`Saga<TData>`** with marker interfaces (`IStartedBy<OrderPlaced>`, `IHandles<OrderCancellationRequested>`, `IHandles<BuyersRemorseExpired>`) — the source generator emits per-message wrappers that load saga state, dispatch to the right `Handle`, persist on success.
- **`[Correlation]`** — declares which message field identifies the saga instance. The framework looks the saga up by `OrderId`; no manual lookup code.
- **`Schedule<T>(delay, message)`** — the saga schedules its own timeout. The wrapper stages the schedule under a key prefixed `saga:<id>:`, so `Complete()` later cancels every still-pending timeout for that saga in one call.
- **`Complete()`** — terminal state. Deletes the saga row + cancels pending timeouts; subsequent messages for the same correlation just see "no saga, ignore" (unless an `IHandleSagaNotFound<T>` is wired).

---

## Running it

```
dotnet run --project samples/saga-timeout
```

Expected output:

```
══════ Run 1: customer waits out the hold ══════
[saga]      hold started for order ... (1s)
[saga]      hold expired — publishing OrderAccepted
[downstream] OrderAccepted received for ...

══════ Run 2: customer cancels mid-hold ══════
[saga]      hold started for order ... (60s)
[saga]      cancel within hold window — saga complete, no OrderAccepted
[saga]      no OrderAccepted fired — hold was absorbed by the cancel
```

---

## Why this shape

### The hold window is business policy, not infrastructure timing

A naive implementation would block the order pipeline for N seconds after every order ("just sleep"), or push the decision to a cron job that scans for "orders older than X". Both push business policy into infrastructure code where the next reader has to reverse-engineer the rule.

A saga makes the policy explicit: this message starts the hold, that message either ends it early or lets it expire. The policy is one class. The schedule is one line. Changing the hold from one minute to ten is a constant edit.

### Cancel and timeout are the same shape

The cancel handler and the timeout handler are both just `IHandles<T>` methods. The saga doesn't care which one wins the race — whichever message arrives first runs to completion, and the other one (if it ever arrives) finds no saga state and is ignored. No locks, no mutex, no "did the timer fire yet?" probes. The framework handles the lookup; the saga handles the policy.

### `Complete()` is what makes the saga a unit of work, not a state machine

A bare state machine would need an explicit "Cancelled" or "Accepted" terminal state plus code to ignore further messages. With `Complete()`, the saga is gone — the row is deleted, pending timeouts are cancelled. Anything that arrives later for the same correlation is unhandled by definition. That's safer than carrying a "DoNotProcess" flag forever.

### Cancellation is cheap because timeouts have predictable keys

The framework prefixes scheduled keys with `saga:<id>:` so `Complete()` can call `CancelByPrefixAsync("saga:<id>:")` and remove every still-pending timeout in one round-trip. Without that convention, a saga that scheduled three different timeouts would have to remember each key and cancel each one individually — easy to get wrong.

### The downstream subscriber doesn't know a saga exists

`OrderAcceptedLogger` listens for `OrderAccepted` and prints a line. It has no idea about `OrderPlaced`, the hold window, or the saga. That's the point of going via an event: the timing decision lives in one place (the saga); the downstream effects are independent subscribers.

---

## What's stand-in vs production

The `InMemorySagaState<T>` and `InMemoryScheduler` types in this sample are demonstration-only — they keep saga state and scheduled messages in process memory. Production replaces them with:

- `Mosaic.Sagas.EFCore` for saga state persistence (auto-registered when the saga's primary constructor takes a `DbContext`).
- `Mosaic.Outbox.EFCore`'s `UseEFCoreScheduling<TDbContext>()` for scheduled messages — atomic with the saga's own state changes via the consumer's `DbContext` change tracker.

The saga code itself is identical between this sample and a production deployment.
