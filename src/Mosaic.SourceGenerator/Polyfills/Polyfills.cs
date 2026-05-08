// Polyfills for compiler-required types missing from netstandard2.0. Roslyn analyzers must
// target netstandard2.0, so we ship the minimal set of types the C# 12+ syntax we use needs.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
