# Mosaic.Runtime

Internal runtime helpers for the [Mosaic](https://github.com/pikedev/mosaic) typed composition + mediator framework — consumed by the source-generated dispatcher.

You don't reference this package directly. The Mosaic meta package (`Mosaic`) pulls it in transitively; the source generator emits code against the types declared here.

What's inside:

- `CompositionContext` — per-invocation context the generator instantiates around each handler call. Carries `MessageId`, `CorrelationId`, `CausationId`, the active `IServiceProvider`, and re-entrant access to the engine.
- `InboundDispatch` — shared resilience helper transports call to deliver an inbound event (DI scope, inbox dedup, recoverability policy loop, dead-letter on exhaustion, audit + critical-error hooks).
- `MosaicBufferWriter` — pooled `IBufferWriter<byte>` over `ArrayPool<byte>.Shared`, used on every publish so serialization doesn't allocate per message.
- Defaults registered by `AddMosaic()`: `InProcessOnlyTransport`, `InMemoryDeadLetterStore`, `DefaultRecoverabilityPolicy`, `LoggingCriticalErrorHandler`, `NoOpMessageAuditStore`, `DefaultMosaicSerializerRegistry` (System.Text.Json, reflection-based).
- Opt-in builder extensions: `UseInMemoryAuditing`, `UseAuditing<T>`, `UseRecoverability(...)`, `UseCriticalErrorHandler<T>`, `UseSystemTextJsonContext(JsonSerializerContext)`.
- Telemetry — `MosaicActivitySource` + tag conventions for the spans the source generator emits.

Not AOT-compatible by default (the JSON registry uses reflection-based S.T.Json). For AOT-clean deployments, swap in `Mosaic.Serialization.MemoryPack` or the source-generated JSON path via `UseSystemTextJsonContext(MyContext.Default)`.
