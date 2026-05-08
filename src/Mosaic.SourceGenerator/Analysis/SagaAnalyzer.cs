using Microsoft.CodeAnalysis;
using Mosaic.SourceGenerator.Diagnostics;
using Mosaic.SourceGenerator.Helpers;
using System.Collections.Immutable;

namespace Mosaic.SourceGenerator.Analysis;

/// <summary>
/// Analyses each compilation looking for classes that derive from <c>Mosaic.Sagas.Saga&lt;TData&gt;</c>
/// and produces <see cref="SagaDispatch"/> records the emitter uses to generate per-message
/// <c>IEventHandler</c> wrappers.
/// </summary>
internal static class SagaAnalyzer
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    /// Walks the assemblies that may contain user sagas and returns one <see cref="SagaDispatch"/>
    /// per discovered saga. Returns an empty result when <c>Mosaic.Sagas</c> isn't referenced.
    /// <para>
    /// The second component is a <c>messageFullName → ITypeSymbol</c> map so the caller can use
    /// the symbol when constructing closed-generic behaviors for events that have only saga
    /// wrappers as handlers (no concrete <see cref="IEventHandler{TEvent}"/>) — without the symbol
    /// the publish-behavior chain can't be closed for that event.
    /// </para>
    /// </summary>
    public static (List<SagaDispatch> Sagas, Dictionary<string, ITypeSymbol> MessageSymbols) Analyze(
        Compilation compilation,
        KnownTypes known,
        Action<Diagnostic> reportDiagnostic)
    {
        if (!known.SagasAvailable)
        {
            return (new List<SagaDispatch>(0), new Dictionary<string, ITypeSymbol>(0));
        }

        var sagas = new List<INamedTypeSymbol>();
        foreach (var assembly in EnumerateRelevantAssemblies(compilation, known))
        {
            VisitNamespace(assembly.GlobalNamespace, known, sagas);
        }

        var result = new List<SagaDispatch>(sagas.Count);
        var messageSymbols = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        foreach (var saga in sagas)
        {
            var dispatch = BuildSagaDispatch(saga, known, reportDiagnostic, messageSymbols);
            if (dispatch is not null) result.Add(dispatch);
        }
        return (result, messageSymbols);
    }

    private static IEnumerable<IAssemblySymbol> EnumerateRelevantAssemblies(Compilation compilation, KnownTypes known)
    {
        yield return compilation.Assembly;
        var sagasAssemblyName = known.SagaT!.ContainingAssembly.Name;
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly) continue;
            if (string.Equals(assembly.Name, sagasAssemblyName, StringComparison.Ordinal)) continue;
            // Walk any assembly that depends on Mosaic.Sagas — that's where user sagas can live.
            foreach (var module in assembly.Modules)
            {
                foreach (var referenced in module.ReferencedAssemblies)
                {
                    if (string.Equals(referenced.Name, sagasAssemblyName, StringComparison.Ordinal))
                    {
                        yield return assembly;
                        goto next;
                    }
                }
            }
            next: ;
        }
    }

    private static void VisitNamespace(INamespaceSymbol ns, KnownTypes known, List<INamedTypeSymbol> sagas)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol child: VisitNamespace(child, known, sagas); break;
                case INamedTypeSymbol type: VisitType(type, known, sagas); break;
            }
        }
    }

    private static void VisitType(INamedTypeSymbol type, KnownTypes known, List<INamedTypeSymbol> sagas)
    {
        foreach (var nested in type.GetTypeMembers()) VisitType(nested, known, sagas);
        if (type.IsAbstract || type.TypeKind != TypeKind.Class) return;
        if (DerivesFromSaga(type, known)) sagas.Add(type);
    }

    private static bool DerivesFromSaga(INamedTypeSymbol type, KnownTypes known)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType
                && SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, known.SagaT))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static INamedTypeSymbol? GetSagaDataTypeArg(INamedTypeSymbol sagaType, KnownTypes known)
    {
        var current = sagaType.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType
                && SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, known.SagaT))
            {
                return current.TypeArguments[0] as INamedTypeSymbol;
            }
            current = current.BaseType;
        }
        return null;
    }

    private static SagaDispatch? BuildSagaDispatch(
        INamedTypeSymbol saga,
        KnownTypes known,
        Action<Diagnostic> report,
        Dictionary<string, ITypeSymbol> messageSymbols)
    {
        var sagaFullName = saga.ToDisplayString(FullyQualifiedFormat);
        var sagaSimpleName = saga.Name;

        // 1. TData
        var data = GetSagaDataTypeArg(saga, known);
        if (data is null) return null;
        if (!InheritsFromSagaData(data, known))
        {
            report(Diagnostic.Create(MosaicDiagnostics.SagaDataNotInheriting, saga.Locations.FirstOrDefault() ?? Location.None,
                sagaFullName, data.ToDisplayString(FullyQualifiedFormat)));
            return null;
        }

        // 2. DbContext from primary constructor — OPTIONAL hint. When present, the registration
        // emitter will auto-wire ISagaStateStore<TData> to the EF Core adapter. When absent, the
        // user must register the saga state store manually (custom backend or in-memory test stub).
        var dbContext = ResolveDbContextFromPrimaryCtor(saga, known);

        // 3. Correlation property on TData (default to "Id" if not marked)
        var (corrPropName, corrPropTypeFullName) = ResolveCorrelationProperty(data, known);

        // 4. Markers + matching Handle methods
        var messages = new List<SagaMessage>();
        var hasStarter = false;
        var seenSimpleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var iface in saga.AllInterfaces)
        {
            if (!iface.IsGenericType) continue;
            var openIface = iface.OriginalDefinition;

            bool isStarter = SymbolEqualityComparer.Default.Equals(openIface, known.IStartedByT);
            bool isHandles = SymbolEqualityComparer.Default.Equals(openIface, known.IHandlesT);
            if (!isStarter && !isHandles) continue;

            var messageType = iface.TypeArguments[0];
            var messageFullName = messageType.ToDisplayString(FullyQualifiedFormat);
            // Track the symbol so the caller can close publish-behaviors for events whose only
            // handler is a saga wrapper (no concrete IEventHandler<X> exists).
            if (!messageSymbols.ContainsKey(messageFullName)) messageSymbols.Add(messageFullName, messageType);

            // Find matching Handle method
            var handle = FindHandleMethod(saga, messageType);
            if (handle is null)
            {
                var markerName = isStarter ? $"IStartedBy<{messageType.Name}>" : $"IHandles<{messageType.Name}>";
                report(Diagnostic.Create(MosaicDiagnostics.SagaMissingHandleMethod, saga.Locations.FirstOrDefault() ?? Location.None,
                    sagaFullName, markerName, messageFullName));
                continue;
            }

            // Correlation: convention or partial
            var hasPartial = HasCorrelateByPartial(saga, messageType);
            var msgPropAccessor = hasPartial ? "" : ResolveMessageCorrelationProperty(messageType, corrPropName, corrPropTypeFullName);
            if (!hasPartial && string.IsNullOrEmpty(msgPropAccessor))
            {
                report(Diagnostic.Create(MosaicDiagnostics.SagaCorrelationNotFound, saga.Locations.FirstOrDefault() ?? Location.None,
                    sagaFullName, messageFullName, corrPropName));
                continue;
            }

            // [During] states
            var states = ReadDuringStates(handle, known);

            if (isStarter) hasStarter = true;

            var simpleName = MakeUnique(messageType.Name, seenSimpleNames);
            messages.Add(new SagaMessage(
                MessageFullName: messageFullName,
                MessageSimpleName: simpleName,
                IsStarter: isStarter,
                CorrelationAccessorOnMessage: msgPropAccessor,
                HasCorrelateByPartial: hasPartial,
                DuringStates: new EquatableArray<string>(states.ToImmutableArray())));
        }

        if (!hasStarter)
        {
            report(Diagnostic.Create(MosaicDiagnostics.SagaWithoutStarter, saga.Locations.FirstOrDefault() ?? Location.None,
                sagaFullName));
            return null;
        }

        return new SagaDispatch(
            SagaFullName: sagaFullName,
            SagaSimpleName: sagaSimpleName,
            DataFullName: data.ToDisplayString(FullyQualifiedFormat),
            DbContextFullName: dbContext?.ToDisplayString(FullyQualifiedFormat) ?? "",
            CorrelationPropertyName: corrPropName,
            CorrelationPropertyTypeFullName: corrPropTypeFullName,
            Messages: new EquatableArray<SagaMessage>(messages.ToImmutableArray()));
    }

    private static bool InheritsFromSagaData(INamedTypeSymbol type, KnownTypes known)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, known.SagaData)) return true;
            current = current.BaseType;
        }
        return false;
    }

    private static INamedTypeSymbol? ResolveDbContextFromPrimaryCtor(INamedTypeSymbol saga, KnownTypes known)
    {
        // Returns null when EF isn't referenced or the saga has no DbContext-typed ctor parameter —
        // both are fine; the user just has to register their own ISagaStateStore<TData>.
        if (known.DbContext is null) return null;
        foreach (var ctor in saga.InstanceConstructors)
        {
            foreach (var param in ctor.Parameters)
            {
                if (param.Type is INamedTypeSymbol named && InheritsFromDbContext(named, known))
                {
                    return named;
                }
            }
        }
        return null;
    }

    private static bool InheritsFromDbContext(INamedTypeSymbol type, KnownTypes known)
    {
        if (known.DbContext is null) return false;
        var current = (INamedTypeSymbol?)type;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, known.DbContext)) return true;
            current = current.BaseType;
        }
        return false;
    }

    private static (string PropertyName, string PropertyTypeFullName) ResolveCorrelationProperty(
        INamedTypeSymbol data,
        KnownTypes known)
    {
        // Walk the type + base classes; first property with [Correlation] wins. Fall back to "Id"
        // (which SagaData defines as Guid).
        var current = (INamedTypeSymbol?)data;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                foreach (var attr in prop.GetAttributes())
                {
                    if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, known.CorrelationAttribute))
                    {
                        return (prop.Name, prop.Type.ToDisplayString(FullyQualifiedFormat));
                    }
                }
            }
            current = current.BaseType;
        }
        // Fallback — SagaData.Id is Guid
        return ("Id", "global::System.Guid");
    }

    private static string ResolveMessageCorrelationProperty(
        ITypeSymbol message,
        string corrName,
        string corrTypeFullName)
    {
        var current = message as INamedTypeSymbol;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop
                    && string.Equals(prop.Name, corrName, StringComparison.Ordinal)
                    && prop.DeclaredAccessibility == Accessibility.Public
                    && string.Equals(prop.Type.ToDisplayString(FullyQualifiedFormat), corrTypeFullName, StringComparison.Ordinal))
                {
                    return prop.Name;
                }
            }
            current = current.BaseType;
        }
        return "";
    }

    private static IMethodSymbol? FindHandleMethod(INamedTypeSymbol saga, ITypeSymbol messageType)
    {
        foreach (var member in saga.GetMembers("Handle"))
        {
            if (member is not IMethodSymbol method) continue;
            if (method.IsStatic) continue;
            if (method.Parameters.Length != 3) continue;
            if (!SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, messageType)) continue;
            // params[1] is ICompositionContext, params[2] is CancellationToken — trust by position+arity.
            return method;
        }
        return null;
    }

    private static bool HasCorrelateByPartial(INamedTypeSymbol saga, ITypeSymbol messageType)
    {
        foreach (var member in saga.GetMembers("CorrelateBy"))
        {
            if (member is not IMethodSymbol method) continue;
            if (!method.IsStatic) continue;
            if (method.Parameters.Length != 1) continue;
            if (!SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, messageType)) continue;
            return true;
        }
        return false;
    }

    private static List<string> ReadDuringStates(IMethodSymbol handle, KnownTypes known)
    {
        var result = new List<string>();
        foreach (var attr in handle.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, known.DuringAttribute)) continue;
            // ctor takes params string[] states
            if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Kind == TypedConstantKind.Array)
            {
                foreach (var v in attr.ConstructorArguments[0].Values)
                {
                    if (v.Value is string s) result.Add(s);
                }
            }
        }
        return result;
    }

    private static string MakeUnique(string baseName, HashSet<string> seen)
    {
        if (seen.Add(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (seen.Add(candidate)) return candidate;
        }
    }
}
