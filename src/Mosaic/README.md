# Mosaic

The meta package — pulls in `Mosaic.Abstractions`, `Mosaic.Runtime`, and the source generator (configured as a build-time analyzer). Reference this in your composition root unless you need finer control over the dependencies.

```csharp
var services = new ServiceCollection();
services.AddMosaic();              // generated extension; discovers every handler in the compilation
var sp = services.BuildServiceProvider();

var engine = sp.GetRequiredService<ICompositionEngine>();
var greeting = await engine.Send(new GetGreeting("world"));
```

Mosaic is a typed, source-generated **mediator + view-model composition** framework for .NET. Each handler contributes a tile; the framework assembles the picture.

- **Send** — `IRequest<TResponse>` to one `IRequestHandler<,>`.
- **Compose** — `IComposable<TViewModel>` to every `IComposer<,>` registered for that VM, in parallel.
- **Publish** — `IEvent` to many `IEventHandler<>` implementations.

The dispatcher is source-generated in your composition root — no reflection at startup, no virtual calls on the hot path, AOT/trim-friendly. Handler discovery scans the consumer's compilation plus every transitively-referenced assembly that depends on `Mosaic.Abstractions`.

Cross-cutting features (atomic outbox, sagas, scheduled messages, cross-process transport, audit pipeline, recoverability policy, critical-error handler, AOT-friendly JSON) are opt-in via additional packages — see https://github.com/pikedev/mosaic for the full layout, design notes, and runnable samples.
