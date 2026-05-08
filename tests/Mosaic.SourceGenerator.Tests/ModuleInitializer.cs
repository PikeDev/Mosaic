using System.Runtime.CompilerServices;
using VerifyXunit;

namespace Mosaic.SourceGenerator.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) =>
            new(directory: System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sourceFile)!, "Snapshots"),
                typeName: type.Name,
                methodName: method.Name));
    }
}
