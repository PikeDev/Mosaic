using Microsoft.CodeAnalysis;

namespace Mosaic.SourceGenerator.Diagnostics;

internal static class MosaicDiagnostics
{
    private const string Category = "Mosaic";

    public static readonly DiagnosticDescriptor RequestWithoutHandler = new(
        id: "MOSAIC0001",
        title: "Request has no handler",
        messageFormat: "IRequest<{0}> '{1}' has no IRequestHandler. Each request must have exactly one handler.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequestWithMultipleHandlers = new(
        id: "MOSAIC0002",
        title: "Request has multiple handlers",
        messageFormat: "IRequest<{0}> '{1}' has {2} handlers; exactly one is required. Handlers: {3}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ComposableWithoutComposers = new(
        id: "MOSAIC0003",
        title: "Composable has no composers",
        messageFormat: "IComposable<{0}> '{1}' has no IComposer implementations. Composing it would always produce an empty view-model.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ComposerViewModelMismatch = new(
        id: "MOSAIC0004",
        title: "Composers disagree on view-model type",
        messageFormat: "Composers for '{0}' target multiple view-model types: {1}. All composers for the same composable must contribute to the same view-model.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EventWithoutHandlers = new(
        id: "MOSAIC0005",
        title: "Event has no handlers",
        messageFormat: "Event '{0}' has no IEventHandler implementations. Publishing it will be a no-op.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MosaicJsonContextMissingEvent = new(
        id: "MOSAIC0006",
        title: "MosaicJsonContext is missing [JsonSerializable] for an IEvent",
        messageFormat: "Mosaic-attributed JsonSerializerContext '{0}' has no [JsonSerializable(typeof({1}))]. The AOT-friendly JSON registry will throw at runtime when this event is published or scheduled.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // ─── Saga diagnostics ──────────────────────────────────────────────────────────────────────

    public static readonly DiagnosticDescriptor SagaWithoutStarter = new(
        id: "MOSAIC_SAGA_001",
        title: "Saga has no IStartedBy<> marker",
        messageFormat: "Saga '{0}' declares no IStartedBy<TMessage> marker. The saga can never be created — add at least one IStartedBy interface to a starter event.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SagaMissingHandleMethod = new(
        id: "MOSAIC_SAGA_002",
        title: "Saga marker has no matching Handle method",
        messageFormat: "Saga '{0}' declares '{1}' but has no matching method 'Handle({2}, ICompositionContext, CancellationToken)'. Add the handler method.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SagaCorrelationNotFound = new(
        id: "MOSAIC_SAGA_003",
        title: "Saga correlation property not found on message",
        messageFormat: "Saga '{0}': message '{1}' has no public property named '{2}' to match the saga's correlation property. Either rename the message property or add 'static partial Guid CorrelateBy({1} m) => …' on the saga.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SagaDataNotInheriting = new(
        id: "MOSAIC_SAGA_004",
        title: "Saga state type doesn't inherit SagaData",
        messageFormat: "Saga '{0}' uses '{1}' as TData. It must inherit Mosaic.Sagas.SagaData.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SagaDbContextNotResolved = new(
        id: "MOSAIC_SAGA_005",
        title: "Saga's DbContext could not be resolved from primary constructor",
        messageFormat: "Saga '{0}' must declare exactly one parameter of a type inheriting Microsoft.EntityFrameworkCore.DbContext via its primary constructor. Found {1}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
