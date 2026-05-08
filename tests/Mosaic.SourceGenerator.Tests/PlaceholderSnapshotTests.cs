using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace Mosaic.SourceGenerator.Tests;

public class GeneratorScaffoldTests
{
    [Fact]
    public void Generator_silently_skips_when_abstractions_not_referenced()
    {
        const string source = """
            namespace SampleConsumer;
            public class Foo { }
            """;

        var compilation = CSharpCompilation.Create(
            assemblyName: "SampleConsumer",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new MosaicGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        result.GeneratedTrees.ShouldBeEmpty();
        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Generator_emits_engine_and_registration_for_request_handler()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mosaic;

            namespace SampleConsumer;

            public sealed record GetGreeting(string Name) : IRequest<string>;

            public sealed class GreetingHandler : IRequestHandler<GetGreeting, string>
            {
                public ValueTask<string> Handle(GetGreeting request, ICompositionContext context, CancellationToken cancellationToken)
                    => new($"Hello, {request.Name}");
            }
            """;

        var compilation = CSharpCompilation.Create(
            assemblyName: "SampleConsumer",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.ValueTask).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Mosaic.IEvent).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new MosaicGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();

        result.Diagnostics.ShouldBeEmpty();
        // Three files: engine, AddMosaic, sagas (sagas file is a placeholder when no sagas exist).
        result.GeneratedTrees.Length.ShouldBe(3);

        var fileNames = result.GeneratedTrees.Select(t => System.IO.Path.GetFileName(t.FilePath)).ToArray();
        fileNames.ShouldContain("Mosaic.GeneratedCompositionEngine.g.cs");
        fileNames.ShouldContain("Mosaic.AddMosaic.g.cs");
        fileNames.ShouldContain("Mosaic.GeneratedSagas.g.cs");

        var engineSource = result.GeneratedTrees.Single(t => t.FilePath.EndsWith("GeneratedCompositionEngine.g.cs", System.StringComparison.Ordinal)).ToString();
        engineSource.ShouldContain("class GeneratedCompositionEngine");
        engineSource.ShouldContain("case global::SampleConsumer.GetGreeting");
        engineSource.ShouldContain("Send_");

        var registrationSource = result.GeneratedTrees.Single(t => t.FilePath.EndsWith("AddMosaic.g.cs", System.StringComparison.Ordinal)).ToString();
        registrationSource.ShouldContain("AddMosaic");
        registrationSource.ShouldContain("SampleConsumer.GreetingHandler");
    }
}
