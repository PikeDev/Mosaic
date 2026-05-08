# Mosaic.Abstractions

Interfaces, attributes, and exceptions for the [Mosaic](https://github.com/pikedev/mosaic) typed composition + mediator framework. Reference this in **every project that defines messages, handlers, or composers**.

This package is intentionally tiny and dependency-light (just `Microsoft.Extensions.DependencyInjection.Abstractions`). It carries no runtime engine and no source generator — those live in `Mosaic.Runtime` and `Mosaic.SourceGenerator` respectively. Most consumers reference the meta package `Mosaic` once in the composition root and reference `Mosaic.Abstractions` everywhere else.

What's in here:

- Message marker interfaces — `IRequest<TResponse>`, `IComposable<TViewModel>`, `IEvent`.
- Handler interfaces — `IRequestHandler<,>`, `IComposer<,>`, `IEventHandler<>`.
- Pipeline behavior interfaces — `IPipelineBehavior<,>`, `IPublishBehavior<>`, `IComposeBehavior<,>`.
- Engine + context surface — `ICompositionEngine`, `ICompositionContext`, `IMosaicBuilder`.
- Wire / persistence seams — `IEventTransport`, `IInboundEventDispatcher`, `IOutboxBuffer`, `IOutboxShipper`, `IInboxStore`, `IScheduledMessageStore`, `IDeadLetterStore`.
- Serialization seam — `IMosaicSerializer<T>`, `IMosaicSerializerRegistry`.
- Operational seams — `IRecoverabilityPolicy`, `ICriticalErrorHandler`, `IMessageAuditStore`.
- Configuration attributes — `[CompositionConfiguration]`, `[Lifetime]`, `[OwnedBy]`, `[MosaicJsonContext]`.

Targets `netstandard2.0` and `net8.0`. AOT-compatible on `net8.0+`.
