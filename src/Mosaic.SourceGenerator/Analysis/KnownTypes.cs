using Microsoft.CodeAnalysis;

namespace Mosaic.SourceGenerator.Analysis;

/// <summary>
/// Resolved <see cref="INamedTypeSymbol"/> handles to the marker interfaces and attributes from
/// <c>Mosaic.Abstractions</c> (and optionally <c>Mosaic.Sagas</c> + <c>Mosaic.Sagas.EFCore</c>).
/// Used during compilation analysis to classify types.
/// </summary>
internal sealed class KnownTypes(
    INamedTypeSymbol iRequestT,
    INamedTypeSymbol iComposableT,
    INamedTypeSymbol iEvent,
    INamedTypeSymbol iRequestHandlerT2,
    INamedTypeSymbol iComposerT2,
    INamedTypeSymbol iEventHandlerT,
    INamedTypeSymbol iPipelineBehaviorT2,
    INamedTypeSymbol iPublishBehaviorT,
    INamedTypeSymbol iComposeBehaviorT2,
    INamedTypeSymbol lifetimeAttribute,
    INamedTypeSymbol compositionConfigurationAttribute,
    INamedTypeSymbol abstractionsAssembly,
    INamedTypeSymbol? sagaT,
    INamedTypeSymbol? sagaData,
    INamedTypeSymbol? iStartedByT,
    INamedTypeSymbol? iHandlesT,
    INamedTypeSymbol? correlationAttribute,
    INamedTypeSymbol? duringAttribute,
    INamedTypeSymbol? sagaStateStoreT,
    INamedTypeSymbol? dbContext,
    INamedTypeSymbol? efCoreSagaStateStoreT2)
{
    public INamedTypeSymbol IRequestT { get; } = iRequestT;
    public INamedTypeSymbol IComposableT { get; } = iComposableT;
    public INamedTypeSymbol IEvent { get; } = iEvent;
    public INamedTypeSymbol IRequestHandlerT2 { get; } = iRequestHandlerT2;
    public INamedTypeSymbol IComposerT2 { get; } = iComposerT2;
    public INamedTypeSymbol IEventHandlerT { get; } = iEventHandlerT;
    public INamedTypeSymbol IPipelineBehaviorT2 { get; } = iPipelineBehaviorT2;
    public INamedTypeSymbol IPublishBehaviorT { get; } = iPublishBehaviorT;
    public INamedTypeSymbol IComposeBehaviorT2 { get; } = iComposeBehaviorT2;
    public INamedTypeSymbol LifetimeAttribute { get; } = lifetimeAttribute;
    public INamedTypeSymbol CompositionConfigurationAttribute { get; } = compositionConfigurationAttribute;
    public INamedTypeSymbol AbstractionsAssembly { get; } = abstractionsAssembly;

    // Saga support — null when Mosaic.Sagas isn't referenced; saga emission is skipped in that case.
    public INamedTypeSymbol? SagaT { get; } = sagaT;                                     // Mosaic.Sagas.Saga`1
    public INamedTypeSymbol? SagaData { get; } = sagaData;                               // Mosaic.Sagas.SagaData
    public INamedTypeSymbol? IStartedByT { get; } = iStartedByT;                         // Mosaic.Sagas.IStartedBy`1
    public INamedTypeSymbol? IHandlesT { get; } = iHandlesT;                             // Mosaic.Sagas.IHandles`1
    public INamedTypeSymbol? CorrelationAttribute { get; } = correlationAttribute;       // Mosaic.Sagas.CorrelationAttribute
    public INamedTypeSymbol? DuringAttribute { get; } = duringAttribute;                 // Mosaic.Sagas.DuringAttribute
    public INamedTypeSymbol? SagaStateStoreT { get; } = sagaStateStoreT;                 // Mosaic.Sagas.ISagaStateStore`1

    // EF Core — null when not referenced. DbContext is consulted as an OPTIONAL hint for the
    // saga's primary constructor: when present + Mosaic.Sagas.EFCore is referenced, the
    // registration emitter auto-wires ISagaStateStore<TData> to the EF Core adapter.
    public INamedTypeSymbol? DbContext { get; } = dbContext;                             // Microsoft.EntityFrameworkCore.DbContext
    public INamedTypeSymbol? EFCoreSagaStateStoreT2 { get; } = efCoreSagaStateStoreT2;   // Mosaic.Sagas.EFCore.EFCoreSagaStateStore`2

    public bool SagasAvailable
        => SagaT is not null && SagaData is not null && IStartedByT is not null && IHandlesT is not null
           && CorrelationAttribute is not null && DuringAttribute is not null
           && SagaStateStoreT is not null;

    /// <summary>True when Mosaic.Sagas.EFCore is referenced — enables the auto-registration of
    /// EF saga state stores for sagas whose primary constructor accepts a DbContext.</summary>
    public bool EFCoreSagaStateStoreAvailable
        => DbContext is not null && EFCoreSagaStateStoreT2 is not null;

    public static KnownTypes? TryResolve(Compilation compilation)
    {
        var iRequest = compilation.GetTypeByMetadataName("Mosaic.IRequest`1");
        var iComposable = compilation.GetTypeByMetadataName("Mosaic.IComposable`1");
        var iEvent = compilation.GetTypeByMetadataName("Mosaic.IEvent");
        var iRequestHandler = compilation.GetTypeByMetadataName("Mosaic.IRequestHandler`2");
        var iComposer = compilation.GetTypeByMetadataName("Mosaic.IComposer`2");
        var iEventHandler = compilation.GetTypeByMetadataName("Mosaic.IEventHandler`1");
        var iPipelineBehavior = compilation.GetTypeByMetadataName("Mosaic.IPipelineBehavior`2");
        var iPublishBehavior = compilation.GetTypeByMetadataName("Mosaic.IPublishBehavior`1");
        var iComposeBehavior = compilation.GetTypeByMetadataName("Mosaic.IComposeBehavior`2");
        var lifetimeAttr = compilation.GetTypeByMetadataName("Mosaic.LifetimeAttribute");
        var compositionConfigAttr = compilation.GetTypeByMetadataName("Mosaic.CompositionConfigurationAttribute");

        if (iRequest is null || iComposable is null || iEvent is null
            || iRequestHandler is null || iComposer is null || iEventHandler is null
            || iPipelineBehavior is null || iPublishBehavior is null || iComposeBehavior is null
            || lifetimeAttr is null
            || compositionConfigAttr is null)
        {
            return null;
        }

        // Optional: Mosaic.Sagas (saga base + markers + attributes + state-store interface).
        var sagaT = compilation.GetTypeByMetadataName("Mosaic.Sagas.Saga`1");
        var sagaData = compilation.GetTypeByMetadataName("Mosaic.Sagas.SagaData");
        var iStartedBy = compilation.GetTypeByMetadataName("Mosaic.Sagas.IStartedBy`1");
        var iHandles = compilation.GetTypeByMetadataName("Mosaic.Sagas.IHandles`1");
        var correlationAttr = compilation.GetTypeByMetadataName("Mosaic.Sagas.CorrelationAttribute");
        var duringAttr = compilation.GetTypeByMetadataName("Mosaic.Sagas.DuringAttribute");
        var sagaStateStore = compilation.GetTypeByMetadataName("Mosaic.Sagas.ISagaStateStore`1");

        // Optional: EF Core DbContext + the EF saga adapter (Mosaic.Sagas.EFCore).
        var dbContext = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        var efSagaStore = compilation.GetTypeByMetadataName("Mosaic.Sagas.EFCore.EFCoreSagaStateStore`2");

        return new KnownTypes(
            iRequestT: iRequest,
            iComposableT: iComposable,
            iEvent: iEvent,
            iRequestHandlerT2: iRequestHandler,
            iComposerT2: iComposer,
            iEventHandlerT: iEventHandler,
            iPipelineBehaviorT2: iPipelineBehavior,
            iPublishBehaviorT: iPublishBehavior,
            iComposeBehaviorT2: iComposeBehavior,
            lifetimeAttribute: lifetimeAttr,
            compositionConfigurationAttribute: compositionConfigAttr,
            abstractionsAssembly: iEvent,
            sagaT: sagaT,
            sagaData: sagaData,
            iStartedByT: iStartedBy,
            iHandlesT: iHandles,
            correlationAttribute: correlationAttr,
            duringAttribute: duringAttr,
            sagaStateStoreT: sagaStateStore,
            dbContext: dbContext,
            efCoreSagaStateStoreT2: efSagaStore);
    }
}
