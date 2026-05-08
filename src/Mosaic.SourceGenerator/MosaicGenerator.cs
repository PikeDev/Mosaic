using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Mosaic.SourceGenerator.Analysis;
using Mosaic.SourceGenerator.Emit;
using System.Text;

namespace Mosaic.SourceGenerator;

/// <summary>
/// The Mosaic incremental source generator.
/// </summary>
/// <remarks>
/// Runs in the consumer's compilation (typically the composition root).
/// Walks the consumer's assembly plus every transitively-referenced assembly that depends on
/// <c>Mosaic.Abstractions</c>, classifies discovered types by which marker interface they implement,
/// validates cardinality, and emits a <c>GeneratedCompositionEngine</c> + <c>AddMosaic()</c>
/// extension into the consumer's binary.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class MosaicGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, GenerateForCompilation);
    }

    private static void GenerateForCompilation(SourceProductionContext spc, Compilation compilation)
    {
        var known = KnownTypes.TryResolve(compilation);
        if (known is null)
        {
            // Mosaic.Abstractions not referenced — silently skip.
            return;
        }

        var catalog = CompilationAnalyzer.Analyze(compilation, known, spc.ReportDiagnostic);

        if (catalog is null)
        {
            return;
        }

        // Always emit, even with zero handlers — keeps consumer-side IDE help correct
        // (the AddMosaic() symbol exists from day one).
        var engineSource = EngineEmitter.Emit(catalog);
        var registrationSource = RegistrationEmitter.Emit(catalog);
        var sagaSource = SagaEmitter.Emit(catalog);

        spc.AddSource("Mosaic.GeneratedCompositionEngine.g.cs", SourceText.From(engineSource, Encoding.UTF8));
        spc.AddSource("Mosaic.AddMosaic.g.cs", SourceText.From(registrationSource, Encoding.UTF8));
        spc.AddSource("Mosaic.GeneratedSagas.g.cs", SourceText.From(sagaSource, Encoding.UTF8));

        // MOSAIC0006: warn when a [MosaicJsonContext]-attributed JsonSerializerContext is missing
        // [JsonSerializable] for an IEvent. No-op when the user hasn't opted in.
        JsonContextCoverageAnalyzer.Run(compilation, catalog, spc.ReportDiagnostic);
    }
}
