using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Mosaic.SourceGenerator.Diagnostics;

namespace Mosaic.SourceGenerator.Analysis;

/// <summary>
/// Reports MOSAIC0006 for every IEvent in the catalog that is not declared via
/// <c>[JsonSerializable(typeof(...))]</c> on a class attributed with
/// <c>[MosaicJsonContext]</c>. Skipped silently when the consumer hasn't opted in (no
/// <c>[MosaicJsonContext]</c>-attributed type exists in their compilation).
/// </summary>
internal static class JsonContextCoverageAnalyzer
{
    public static void Run(Compilation compilation, HandlerCatalog catalog, System.Action<Diagnostic> report)
    {
        var mosaicCtxAttr = compilation.GetTypeByMetadataName("Mosaic.MosaicJsonContextAttribute");
        var jsonSerializableAttr = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializableAttribute");
        if (mosaicCtxAttr is null || jsonSerializableAttr is null) return;

        // Only scan the consumer's own source module — JsonSerializerContext partials always live
        // in the consumer's composition root, not in NuGet dependencies.
        var contexts = FindMosaicAttributedTypes(compilation.SourceModule.GlobalNamespace, mosaicCtxAttr);
        if (contexts.Count == 0) return;

        // Catalog event names look like "global::Sample.OrderPlaced". Build a map of stripped name
        // → display-name for diagnostic message generation, plus the symbol comparison set.
        var catalogEventDisplayNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var ev in catalog.Events)
        {
            catalogEventDisplayNames.Add(StripGlobalPrefix(ev.EventFullName));
        }

        foreach (var ctx in contexts)
        {
            var declaredTypes = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var attr in ctx.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, jsonSerializableAttr)) continue;
                if (attr.ConstructorArguments.Length == 0) continue;
                if (attr.ConstructorArguments[0].Value is INamedTypeSymbol declared)
                {
                    declaredTypes.Add(declared.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Substring("global::".Length));
                }
            }

            var location = ctx.Locations.Length > 0 ? ctx.Locations[0] : Location.None;
            foreach (var eventName in catalogEventDisplayNames)
            {
                if (!declaredTypes.Contains(eventName))
                {
                    report(Diagnostic.Create(
                        MosaicDiagnostics.MosaicJsonContextMissingEvent,
                        location,
                        ctx.Name,
                        eventName));
                }
            }
        }
    }

    private static List<INamedTypeSymbol> FindMosaicAttributedTypes(INamespaceSymbol root, INamedTypeSymbol mosaicCtxAttr)
    {
        var matches = new List<INamedTypeSymbol>();
        Walk(root, matches, mosaicCtxAttr);
        return matches;

        static void Walk(INamespaceSymbol ns, List<INamedTypeSymbol> matches, INamedTypeSymbol mosaicCtxAttr)
        {
            foreach (var t in ns.GetTypeMembers())
            {
                Visit(t, matches, mosaicCtxAttr);
            }
            foreach (var child in ns.GetNamespaceMembers())
            {
                Walk(child, matches, mosaicCtxAttr);
            }
        }

        static void Visit(INamedTypeSymbol t, List<INamedTypeSymbol> matches, INamedTypeSymbol mosaicCtxAttr)
        {
            foreach (var attr in t.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, mosaicCtxAttr))
                {
                    matches.Add(t);
                    break;
                }
            }
            foreach (var nested in t.GetTypeMembers())
            {
                Visit(nested, matches, mosaicCtxAttr);
            }
        }
    }

    private static string StripGlobalPrefix(string fullyQualified)
        => fullyQualified.StartsWith("global::", System.StringComparison.Ordinal)
            ? fullyQualified.Substring("global::".Length)
            : fullyQualified;
}
