using Microsoft.CodeAnalysis;
using Mosaic.SourceGenerator.Diagnostics;
using Mosaic.SourceGenerator.Helpers;
using System.Collections.Immutable;

namespace Mosaic.SourceGenerator.Analysis;

internal static class CompilationAnalyzer
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    /// Same as <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> but emits the <c>?</c>
    /// for annotated nullable reference types. Used when projecting message-shape types into the
    /// generated dispatcher signatures, so a handler returning <c>string?</c> shows up as
    /// <c>ValueTask&lt;string?&gt;</c> in the generated inner method — preserving the contract
    /// the author intended and keeping the consumer's nullable-flow analysis honest.
    /// </summary>
    private static readonly SymbolDisplayFormat NullableFlowingFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Walks the compilation and every transitively-referenced assembly that depends on
    /// <c>Mosaic.Abstractions</c>, classifies discovered types by their implemented marker interfaces,
    /// validates cardinality, and returns the data the emitters need.
    /// </summary>
    public static HandlerCatalog? Analyze(
        Compilation compilation,
        KnownTypes known,
        Action<Diagnostic> reportDiagnostic)
    {
        // Read [assembly: CompositionConfiguration(...)] settings once up front.
        var config = ReadConfiguration(compilation, known);

        var requestHandlers = new List<RequestHandlerCandidate>();
        var composers = new List<ComposerCandidate>();
        var eventHandlers = new List<EventHandlerCandidate>();

        foreach (var assembly in EnumerateRelevantAssemblies(compilation, known))
        {
            VisitNamespace(assembly.GlobalNamespace, known, requestHandlers, composers, eventHandlers, config.DefaultLifetime);
        }

        // Saga analysis: generate SagaDispatch records and synthetic event-handler candidates
        // pointing at the wrapper classes the SagaEmitter is about to produce — so the generated
        // engine.Publish dispatch fans out to those wrappers automatically.
        var ns = !string.IsNullOrWhiteSpace(config.GeneratedNamespace)
            ? config.GeneratedNamespace!
            : (compilation.AssemblyName ?? "Mosaic") + ".Generated.Mosaic";
        var sagaResult = SagaAnalyzer.Analyze(compilation, known, reportDiagnostic);
        var sagas = sagaResult.Sagas;
        var sagaMessageSymbols = sagaResult.MessageSymbols;
        foreach (var saga in sagas)
        {
            foreach (var msg in saga.Messages)
            {
                sagaMessageSymbols.TryGetValue(msg.MessageFullName, out var msgSymbol);
                eventHandlers.Add(new EventHandlerCandidate(
                    HandlerFullName: $"global::{ns}.{saga.SagaSimpleName}_{msg.MessageSimpleName}_Handler",
                    EventFullName: msg.MessageFullName,
                    EventSimpleName: msg.MessageSimpleName,
                    Lifetime: ServiceLifetime.Scoped,
                    EventSymbol: msgSymbol));
            }
        }

        // Group + validate
        var requests = ValidateAndBuildRequests(requestHandlers, config.PipelineBehaviors, reportDiagnostic);
        var composables = ValidateAndBuildComposables(composers, config.ComposeBehaviors, reportDiagnostic);
        var events = BuildEvents(eventHandlers, config.PublishBehaviors);

        // Track which request types had multiple handlers (validation rejected) so we don't emit
        // their handler registrations either — keeps the generated AddMosaic consistent with the
        // dispatcher (which already skips them).
        var skippedRequestTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in requestHandlers.GroupBy(h => h.RequestFullName, StringComparer.Ordinal))
        {
            if (group.Count() > 1) skippedRequestTypes.Add(group.Key);
        }

        var registrations = BuildRegistrations(
            requestHandlers, composers, eventHandlers, requests, composables, events,
            config.DefaultLifetime, skippedRequestTypes);

        // Saga + wrapper registrations: each saga is Scoped; each wrapper is Scoped too (DI fan-out
        // by IEventHandler<TMessage> is what makes engine.Publish reach them).
        foreach (var saga in sagas)
        {
            registrations.Add(new HandlerRegistration(
                ServiceTypeFullName: saga.SagaFullName,
                ImplementationFullName: saga.SagaFullName,
                Lifetime: ServiceLifetime.Scoped.ToString()));
            // Wrapper registrations are added as IEventHandler<TMessage> bindings via the synthetic
            // event-handler candidates above, which BuildRegistrations already turned into entries.
        }

        return new HandlerCatalog(
            GeneratedNamespace: ns,
            EventPublishMode: config.EventPublishMode.ToString(),
            EmitTelemetry: config.EmitTelemetry,
            EFCoreSagaStateStoreAvailable: known.EFCoreSagaStateStoreAvailable,
            Requests: new EquatableArray<RequestDispatch>(requests),
            Composables: new EquatableArray<ComposableDispatch>(composables),
            Events: new EquatableArray<EventDispatch>(events),
            Registrations: new EquatableArray<HandlerRegistration>(registrations),
            Sagas: new EquatableArray<SagaDispatch>(ImmutableArray.CreateRange<SagaDispatch>(sagas)));
    }

    // ─── Symbol walking ────────────────────────────────────────────────────────────────────────

    private static IEnumerable<IAssemblySymbol> EnumerateRelevantAssemblies(Compilation compilation, KnownTypes known)
    {
        // Always include the consumer's own assembly
        yield return compilation.Assembly;

        // Plus every transitively-referenced assembly that depends on Mosaic.Abstractions
        var abstractionsAssemblyName = known.AbstractionsAssembly.ContainingAssembly.Name;

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            // Skip the abstractions assembly itself — no handlers there
            if (string.Equals(assembly.Name, abstractionsAssemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            if (DependsOn(assembly, abstractionsAssemblyName))
            {
                yield return assembly;
            }
        }
    }

    private static bool DependsOn(IAssemblySymbol assembly, string targetAssemblyName)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var referenced in module.ReferencedAssemblies)
            {
                if (string.Equals(referenced.Name, targetAssemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void VisitNamespace(
        INamespaceSymbol ns,
        KnownTypes known,
        List<RequestHandlerCandidate> requestHandlers,
        List<ComposerCandidate> composers,
        List<EventHandlerCandidate> eventHandlers,
        ServiceLifetime defaultLifetime)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    VisitNamespace(childNs, known, requestHandlers, composers, eventHandlers, defaultLifetime);
                    break;
                case INamedTypeSymbol type:
                    VisitType(type, known, requestHandlers, composers, eventHandlers, defaultLifetime);
                    break;
            }
        }
    }

    private static void VisitType(
        INamedTypeSymbol type,
        KnownTypes known,
        List<RequestHandlerCandidate> requestHandlers,
        List<ComposerCandidate> composers,
        List<EventHandlerCandidate> eventHandlers,
        ServiceLifetime defaultLifetime)
    {
        // Recurse into nested types
        foreach (var nested in type.GetTypeMembers())
        {
            VisitType(nested, known, requestHandlers, composers, eventHandlers, defaultLifetime);
        }

        if (type.IsAbstract || type.TypeKind != TypeKind.Class)
        {
            return;
        }

        var lifetime = ReadLifetime(type, known.LifetimeAttribute) ?? defaultLifetime;
        var typeFullName = type.ToDisplayString(FullyQualifiedFormat);

        foreach (var iface in type.AllInterfaces)
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var openIface = iface.OriginalDefinition;

            if (SymbolEqualityComparer.Default.Equals(openIface, known.IRequestHandlerT2))
            {
                var requestType = iface.TypeArguments[0];
                var responseType = iface.TypeArguments[1];
                requestHandlers.Add(new RequestHandlerCandidate(
                    HandlerFullName: typeFullName,
                    RequestFullName: requestType.ToDisplayString(NullableFlowingFormat),
                    ResponseFullName: responseType.ToDisplayString(NullableFlowingFormat),
                    RequestSimpleName: requestType.Name,
                    Lifetime: lifetime,
                    RequestSymbol: requestType,
                    ResponseSymbol: responseType));
            }
            else if (SymbolEqualityComparer.Default.Equals(openIface, known.IComposerT2))
            {
                var requestType = iface.TypeArguments[0];
                var vmType = iface.TypeArguments[1];
                composers.Add(new ComposerCandidate(
                    HandlerFullName: typeFullName,
                    RequestFullName: requestType.ToDisplayString(NullableFlowingFormat),
                    ViewModelFullName: vmType.ToDisplayString(NullableFlowingFormat),
                    RequestSimpleName: requestType.Name,
                    Lifetime: lifetime,
                    RequestSymbol: requestType,
                    ViewModelSymbol: vmType));
            }
            else if (SymbolEqualityComparer.Default.Equals(openIface, known.IEventHandlerT))
            {
                var eventType = iface.TypeArguments[0];
                eventHandlers.Add(new EventHandlerCandidate(
                    HandlerFullName: typeFullName,
                    EventFullName: eventType.ToDisplayString(NullableFlowingFormat),
                    EventSimpleName: eventType.Name,
                    Lifetime: lifetime,
                    EventSymbol: eventType));
            }
        }
    }

    /// <summary>
    /// Reads every setting from <c>[assembly: CompositionConfiguration(...)]</c>. Returns defaults
    /// matching the attribute's own defaults if the attribute is not present.
    /// </summary>
    private static MosaicConfiguration ReadConfiguration(Compilation compilation, KnownTypes known)
    {
        var defaultLifetime = ServiceLifetime.Scoped;
        var publishMode = EventPublishMode.Buffered;
        var emitTelemetry = true;
        string? generatedNamespace = null;
        IReadOnlyList<INamedTypeSymbol> pipelineBehaviors = Array.Empty<INamedTypeSymbol>();
        IReadOnlyList<INamedTypeSymbol> publishBehaviors = Array.Empty<INamedTypeSymbol>();
        IReadOnlyList<INamedTypeSymbol> composeBehaviors = Array.Empty<INamedTypeSymbol>();

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, known.CompositionConfigurationAttribute))
            {
                continue;
            }

            foreach (var named in attr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "DefaultLifetime":
                        if (named.Value.Value is int lt) defaultLifetime = (ServiceLifetime)lt;
                        break;
                    case "EventPublishMode":
                        if (named.Value.Value is int pm) publishMode = (EventPublishMode)pm;
                        break;
                    case "EmitTelemetry":
                        if (named.Value.Value is bool et) emitTelemetry = et;
                        break;
                    case "GeneratedNamespace":
                        if (named.Value.Value is string gn && !string.IsNullOrWhiteSpace(gn)) generatedNamespace = gn;
                        break;
                    case "PipelineBehaviors":
                        pipelineBehaviors = ReadBehaviorsArray(named.Value, known.IPipelineBehaviorT2);
                        break;
                    case "PublishBehaviors":
                        publishBehaviors = ReadBehaviorsArray(named.Value, known.IPublishBehaviorT);
                        break;
                    case "ComposeBehaviors":
                        composeBehaviors = ReadBehaviorsArray(named.Value, known.IComposeBehaviorT2);
                        break;
                }
            }

            break; // Attribute is single-use (AllowMultiple = false).
        }

        return new MosaicConfiguration(defaultLifetime, publishMode, emitTelemetry, generatedNamespace,
            pipelineBehaviors, publishBehaviors, composeBehaviors);
    }

    private static IReadOnlyList<INamedTypeSymbol> ReadBehaviorsArray(TypedConstant value, INamedTypeSymbol expectedOpenInterface)
    {
        if (value.Kind != TypedConstantKind.Array)
        {
            return Array.Empty<INamedTypeSymbol>();
        }

        var result = new List<INamedTypeSymbol>();
        foreach (var item in value.Values)
        {
            if (item.Kind == TypedConstantKind.Type
                && item.Value is INamedTypeSymbol type
                && type.IsGenericType
                && type.IsUnboundGenericType is false
                && ImplementsOpenInterface(type, expectedOpenInterface))
            {
                result.Add(type.OriginalDefinition);
            }
            else if (item.Kind == TypedConstantKind.Type
                     && item.Value is INamedTypeSymbol unbound
                     && unbound.IsUnboundGenericType
                     && ImplementsOpenInterface(unbound.OriginalDefinition, expectedOpenInterface))
            {
                result.Add(unbound.OriginalDefinition);
            }
        }
        return result;
    }

    private sealed record MosaicConfiguration(
        ServiceLifetime DefaultLifetime,
        EventPublishMode EventPublishMode,
        bool EmitTelemetry,
        string? GeneratedNamespace,
        IReadOnlyList<INamedTypeSymbol> PipelineBehaviors,
        IReadOnlyList<INamedTypeSymbol> PublishBehaviors,
        IReadOnlyList<INamedTypeSymbol> ComposeBehaviors);

    private static bool ImplementsOpenInterface(INamedTypeSymbol type, INamedTypeSymbol openInterface)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, openInterface))
            {
                return true;
            }
        }
        return false;
    }

    private static ServiceLifetime? ReadLifetime(INamedTypeSymbol type, INamedTypeSymbol lifetimeAttr)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, lifetimeAttr))
            {
                continue;
            }

            if (attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is int rawValue)
            {
                return (ServiceLifetime)rawValue;
            }
        }
        return null;
    }

    // ─── Validation + grouping ─────────────────────────────────────────────────────────────────

    private static List<RequestDispatch> ValidateAndBuildRequests(
        List<RequestHandlerCandidate> handlers,
        IReadOnlyList<INamedTypeSymbol> openBehaviors,
        Action<Diagnostic> report)
    {
        var byRequest = handlers
            .GroupBy(h => h.RequestFullName, StringComparer.Ordinal)
            .ToList();

        var dispatches = new List<RequestDispatch>(byRequest.Count);
        var seenMethodNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byRequest)
        {
            var first = group.First();
            var count = group.Count();

            if (count > 1)
            {
                var handlerList = string.Join(", ", group.Select(g => g.HandlerFullName));
                report(Diagnostic.Create(
                    MosaicDiagnostics.RequestWithMultipleHandlers,
                    Location.None,
                    first.ResponseFullName,
                    first.RequestFullName,
                    count,
                    handlerList));
                continue;
            }

            // Close each open-generic behavior with this request's (TRequest, TResponse). Use the
            // NRT-flowing format so a behavior closed over a nullable response (e.g.
            // LoggingBehavior<GetUser, User?>) renders accurately in the generated dispatcher.
            var closedBehaviors = ImmutableArray.CreateBuilder<string>(openBehaviors.Count);
            foreach (var openBehavior in openBehaviors)
            {
                var closed = openBehavior.Construct(first.RequestSymbol, first.ResponseSymbol);
                closedBehaviors.Add(closed.ToDisplayString(NullableFlowingFormat));
            }

            dispatches.Add(new RequestDispatch(
                RequestFullName: first.RequestFullName,
                ResponseFullName: first.ResponseFullName,
                HandlerFullName: first.HandlerFullName,
                MethodSafeName: MakeUniqueIdentifier(first.RequestSimpleName, seenMethodNames),
                BehaviorClosedFullNames: new EquatableArray<string>(closedBehaviors.ToImmutable())));
        }

        // Note: MOSAIC0001 (request without handler) requires also discovering IRequest<T> message
        // types themselves. Deferred to a follow-up pass — for v0.1 the runtime exception
        // SendNoHandlerException covers the missing-handler case.

        return dispatches;
    }

    private static List<ComposableDispatch> ValidateAndBuildComposables(
        List<ComposerCandidate> composers,
        IReadOnlyList<INamedTypeSymbol> openComposeBehaviors,
        Action<Diagnostic> report)
    {
        var byRequest = composers
            .GroupBy(c => c.RequestFullName, StringComparer.Ordinal)
            .ToList();

        var dispatches = new List<ComposableDispatch>(byRequest.Count);
        var seenMethodNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byRequest)
        {
            var distinctVms = group.Select(g => g.ViewModelFullName).Distinct(StringComparer.Ordinal).ToList();

            if (distinctVms.Count > 1)
            {
                report(Diagnostic.Create(
                    MosaicDiagnostics.ComposerViewModelMismatch,
                    Location.None,
                    group.Key,
                    string.Join(", ", distinctVms)));
                continue;
            }

            var first = group.First();

            // Close each open-generic compose-behavior with this composable's (TRequest, TViewModel).
            var closedBehaviors = ImmutableArray.CreateBuilder<string>(openComposeBehaviors.Count);
            foreach (var openBehavior in openComposeBehaviors)
            {
                var closed = openBehavior.Construct(first.RequestSymbol, first.ViewModelSymbol);
                closedBehaviors.Add(closed.ToDisplayString(NullableFlowingFormat));
            }

            dispatches.Add(new ComposableDispatch(
                RequestFullName: first.RequestFullName,
                ViewModelFullName: first.ViewModelFullName,
                ComposerFullNames: new EquatableArray<string>(group.Select(g => g.HandlerFullName).ToImmutableArray()),
                MethodSafeName: MakeUniqueIdentifier(first.RequestSimpleName, seenMethodNames),
                BehaviorClosedFullNames: new EquatableArray<string>(closedBehaviors.ToImmutable())));
        }

        return dispatches;
    }

    private static List<EventDispatch> BuildEvents(
        List<EventHandlerCandidate> handlers,
        IReadOnlyList<INamedTypeSymbol> openPublishBehaviors)
    {
        var byEvent = handlers
            .GroupBy(h => h.EventFullName, StringComparer.Ordinal)
            .ToList();

        var dispatches = new List<EventDispatch>(byEvent.Count);
        var seenMethodNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byEvent)
        {
            var first = group.First();

            // Close each open-generic publish-behavior with this event's TEvent. The symbol may be
            // null when every candidate for this event is synthetic (e.g. saga wrappers) — in that
            // case fall back to the first candidate that does carry one. If none do, behaviors
            // can't be closed for this event; skip emitting them.
            var symbol = group.Select(g => g.EventSymbol).FirstOrDefault(s => s is not null);
            var closedBehaviors = ImmutableArray.CreateBuilder<string>(openPublishBehaviors.Count);
            if (symbol is not null)
            {
                foreach (var openBehavior in openPublishBehaviors)
                {
                    var closed = openBehavior.Construct(symbol);
                    closedBehaviors.Add(closed.ToDisplayString(NullableFlowingFormat));
                }
            }

            dispatches.Add(new EventDispatch(
                EventFullName: first.EventFullName,
                HandlerFullNames: new EquatableArray<string>(group.Select(g => g.HandlerFullName).ToImmutableArray()),
                MethodSafeName: MakeUniqueIdentifier(first.EventSimpleName, seenMethodNames),
                BehaviorClosedFullNames: new EquatableArray<string>(closedBehaviors.ToImmutable())));
        }

        return dispatches;
    }

    private static List<HandlerRegistration> BuildRegistrations(
        List<RequestHandlerCandidate> requestHandlers,
        List<ComposerCandidate> composers,
        List<EventHandlerCandidate> eventHandlers,
        List<RequestDispatch> requestDispatches,
        List<ComposableDispatch> composableDispatches,
        List<EventDispatch> eventDispatches,
        ServiceLifetime defaultLifetime,
        HashSet<string> skippedRequestTypes)
    {
        var registrations = new List<HandlerRegistration>(requestHandlers.Count + composers.Count + eventHandlers.Count);

        foreach (var rh in requestHandlers)
        {
            // If validation rejected this request type (e.g. multiple handlers — MOSAIC0002),
            // the dispatcher won't have a switch case for it; skip the registration too so the
            // generated AddMosaic stays consistent with what the engine can actually handle.
            if (skippedRequestTypes.Contains(rh.RequestFullName)) continue;

            // Register both as the IRequestHandler<,> interface AND as the concrete type
            registrations.Add(new HandlerRegistration(
                ServiceTypeFullName: $"global::Mosaic.IRequestHandler<{rh.RequestFullName}, {rh.ResponseFullName}>",
                ImplementationFullName: rh.HandlerFullName,
                Lifetime: rh.Lifetime.ToString()));
        }

        foreach (var c in composers)
        {
            // Composers are registered as concrete types — generated dispatcher resolves them by class
            registrations.Add(new HandlerRegistration(
                ServiceTypeFullName: c.HandlerFullName,
                ImplementationFullName: c.HandlerFullName,
                Lifetime: c.Lifetime.ToString()));
        }

        foreach (var eh in eventHandlers)
        {
            registrations.Add(new HandlerRegistration(
                ServiceTypeFullName: eh.HandlerFullName,
                ImplementationFullName: eh.HandlerFullName,
                Lifetime: eh.Lifetime.ToString()));
        }

        // Each (behavior × message) closed-generic combination needs its own DI registration
        // since the generated dispatcher resolves them by closed concrete type. De-dup across
        // dispatches so the same closed name isn't added twice. One pass per behavior kind
        // (pipeline / publish / compose) but they all share the same de-dup set.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dispatch in requestDispatches)
        {
            foreach (var closedBehavior in dispatch.BehaviorClosedFullNames)
            {
                if (seen.Add(closedBehavior))
                {
                    registrations.Add(new HandlerRegistration(
                        ServiceTypeFullName: closedBehavior,
                        ImplementationFullName: closedBehavior,
                        Lifetime: defaultLifetime.ToString()));
                }
            }
        }
        foreach (var dispatch in eventDispatches)
        {
            foreach (var closedBehavior in dispatch.BehaviorClosedFullNames)
            {
                if (seen.Add(closedBehavior))
                {
                    registrations.Add(new HandlerRegistration(
                        ServiceTypeFullName: closedBehavior,
                        ImplementationFullName: closedBehavior,
                        Lifetime: defaultLifetime.ToString()));
                }
            }
        }
        foreach (var dispatch in composableDispatches)
        {
            foreach (var closedBehavior in dispatch.BehaviorClosedFullNames)
            {
                if (seen.Add(closedBehavior))
                {
                    registrations.Add(new HandlerRegistration(
                        ServiceTypeFullName: closedBehavior,
                        ImplementationFullName: closedBehavior,
                        Lifetime: defaultLifetime.ToString()));
                }
            }
        }

        return registrations;
    }

    private static string MakeUniqueIdentifier(string baseName, HashSet<string> seen)
    {
        var sanitized = SanitizeForIdentifier(baseName);
        if (seen.Add(sanitized))
        {
            return sanitized;
        }

        for (int i = 2; ; i++)
        {
            var candidate = $"{sanitized}_{i}";
            if (seen.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeForIdentifier(string name)
    {
        // Strip any chars that aren't valid in a method name. Simple types' Name is always fine,
        // but constructed generics or nested types could in theory carry separators.
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
            {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }

    // ─── Internal candidate records (intermediate; not part of HandlerCatalog) ────────────────

    internal sealed record RequestHandlerCandidate(
        string HandlerFullName,
        string RequestFullName,
        string ResponseFullName,
        string RequestSimpleName,
        ServiceLifetime Lifetime,
        ITypeSymbol RequestSymbol,
        ITypeSymbol ResponseSymbol);

    internal sealed record ComposerCandidate(
        string HandlerFullName,
        string RequestFullName,
        string ViewModelFullName,
        string RequestSimpleName,
        ServiceLifetime Lifetime,
        ITypeSymbol RequestSymbol,
        ITypeSymbol ViewModelSymbol);

    internal sealed record EventHandlerCandidate(
        string HandlerFullName,
        string EventFullName,
        string EventSimpleName,
        ServiceLifetime Lifetime,
        ITypeSymbol? EventSymbol = null);
}

/// <summary>
/// Local mirror of <c>Microsoft.Extensions.DependencyInjection.ServiceLifetime</c>. The generator
/// does not reference MEDI to keep the analyzer assembly tiny; values match the MEDI enum.
/// </summary>
internal enum ServiceLifetime
{
    Singleton = 0,
    Scoped = 1,
    Transient = 2,
}

/// <summary>Local mirror of <c>Mosaic.EventPublishMode</c>; values match the abstractions enum.</summary>
internal enum EventPublishMode
{
    Buffered = 0,
    Eager = 1,
}
