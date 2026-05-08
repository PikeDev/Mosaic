# Changelog

All notable changes to Mosaic are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

## [1.0.0-rc.1] — 2026-05-08

Initial public release candidate.

### Core

- **Send** (`IRequest<TResponse>` → one `IRequestHandler<,>`), **Compose** (`IComposable<TViewModel>` → many parallel `IComposer<,>`s writing slices of one VM), **Publish** (`IEvent` → many `IEventHandler<>`s).
- Source-generated dispatcher (`IIncrementalGenerator`) emitted in the consumer's composition root — no reflection at startup, no virtual calls on the hot path, AOT/trim-friendly.
- Handler discovery walks the composition root + every transitively-referenced assembly that depends on `Mosaic.Abstractions`. The framework projects never reference handler projects.
- Pluggable handler lifetimes (Singleton / Scoped / Transient) via `[Lifetime]`, honored at dispatch time.
- Pipeline behaviors for Send / Publish / Compose, declared via `[CompositionConfiguration]` on the composition root.
- Per-handler DI sub-scopes for parallel event handlers — handlers that share a scoped service (typically a DbContext) don't fight over its state.
- Buffered (default) and Eager event publish modes.

### Sagas

- `Saga<TData>` with marker interfaces (`IStartedBy<>`, `IHandles<>`), the `[Correlation]` attribute, `[During(state)]` guards, `Schedule<T>` for self-timeouts, `Complete()` lifecycle.
- `Mosaic.Sagas.EFCore` — auto-registered EF Core saga state store for sagas whose primary constructor takes a `DbContext`.

### Reliability + delivery

- Atomic outbox via `Mosaic.Outbox.EFCore` (`UseEFCoreOutbox<TDbContext>()`) — `Publish` writes a row into the consumer's DbContext change tracker; commits with state in one transaction.
- Inbox dedup keyed on the wire `MessageId`.
- Scheduled messages via `UseEFCoreScheduling<TDbContext>()` (also atomic with consumer state).
- Standalone `Mosaic.Outbox.Postgres` for consumers without EF Core.
- `Mosaic.Transport.Postgres` (LISTEN/NOTIFY) and `Mosaic.Transport.InMemory` (tests + same-host topologies).
- `IOutboxShipper` abstraction — outbox relays are transport-agnostic; transport packages register their own shipper.

### Observability + operations

- Correlation graph: every message carries `MessageHeaders` (`MessageId` / `CorrelationId` / `CausationId` / `SentAtUtc`); the engine stitches the chain across in-process and transport hops.
- `IMessageAuditStore` opt-in via `UseAuditing<TStore>()` / `UseInMemoryAuditing()`.
- `IRecoverabilityPolicy` per-exception rules via `UseRecoverability(decideFn)`; replaces the historical retry-then-DLQ default.
- `ICriticalErrorHandler` for catastrophic conditions (DLQ outage, etc.) via `UseCriticalErrorHandler<T>()`.

### Serialization

- `IMosaicSerializerRegistry` abstraction; default is reflection-based System.Text.Json.
- `Mosaic.Serialization.MemoryPack` adapter for binary MemoryPack on the wire.
- AOT-friendly path: declare `[MosaicJsonContext] partial JsonSerializerContext` and call `UseSystemTextJsonContext(MyContext.Default)`. Analyzer MOSAIC0006 warns when the context is missing `[JsonSerializable]` for any IEvent in the catalog.
- Pooled `MosaicBufferWriter` (over `ArrayPool<byte>.Shared`) on the publish path so serialization doesn't allocate per message.

### Test harness

- `Mosaic.Testing` — `MosaicTestHarness` + `AddMosaicTestHarness()` for asserting on engine traffic without a transport.

### Diagnostics

- MOSAIC0001..0006 (request without handler, multiple handlers, composable without composers, composer VM mismatch, event without handlers, JSON context missing serializable).
- MOSAIC_SAGA_001..005 (no IStartedBy, missing Handle method, correlation property not found, SagaData inheritance, DbContext resolution).

[1.0.0-rc.1]: https://github.com/pikedev/mosaic/releases/tag/v1.0.0-rc.1
