using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mosaic.SourceGenerator.Tests;

internal static class TestCompilationFactory
{
    /// <summary>
    /// Creates a compilation that references the bare framework (object/runtime) plus
    /// <c>Mosaic.Abstractions</c>, so the generator's <c>KnownTypes.TryResolve</c> succeeds.
    /// </summary>
    private static readonly Lazy<MetadataReference[]> RuntimeReferences = new(() =>
    {
        // Pull in every assembly the test host has loaded — gives us the full BCL plus
        // Mosaic.Abstractions and MEDI.Abstractions without manually enumerating them.
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var paths = trusted.Split(System.IO.Path.PathSeparator)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();

        var refs = new List<MetadataReference>(paths.Length);
        foreach (var path in paths)
        {
            try
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
            catch
            {
                // Some TPA entries (resource-only DLLs) cannot be loaded as metadata refs — skip them.
            }
        }

        // Make sure Mosaic.Abstractions, MEDI.Abstractions, Mosaic.Sagas + EF Core are present
        // even if not on the TPA list — saga fixtures reference Saga<T>, [Correlation], DbContext.
        AddIfMissing(refs, typeof(Mosaic.IEvent).Assembly.Location);
        AddIfMissing(refs, typeof(Microsoft.Extensions.DependencyInjection.ServiceLifetime).Assembly.Location);
        AddIfMissing(refs, typeof(Mosaic.Sagas.SagaData).Assembly.Location);
        AddIfMissing(refs, typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location);
        return refs.ToArray();
    });

    private static void AddIfMissing(List<MetadataReference> refs, string path)
    {
        if (!refs.OfType<PortableExecutableReference>().Any(r => string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            refs.Add(MetadataReference.CreateFromFile(path));
        }
    }

    public static CSharpCompilation Create(string source, string assemblyName = "TestAssembly", bool nullable = false)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        if (nullable) options = options.WithNullableContextOptions(NullableContextOptions.Enable);
        return CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: RuntimeReferences.Value,
            options: options);
    }

    public static GeneratorDriver RunGenerator(CSharpCompilation compilation)
    {
        // Surface compile errors in the input fixture immediately — otherwise the generator
        // sees half-bound types (e.g. attributes whose enum arg never resolved) and silently
        // produces wrong output, which then gets baked into the snapshot.
        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (diagnostics.Count > 0)
        {
            throw new InvalidOperationException(
                "Test fixture failed to compile:\n" + string.Join("\n", diagnostics.Select(d => d.ToString())));
        }

        var generator = new MosaicGenerator();
        return CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);
    }
}
