// Polyfill required for `init`-only setters and records under netstandard2.0.
// The C# compiler emits references to System.Runtime.CompilerServices.IsExternalInit
// for any init-only setter; this type ships in the BCL from .NET 5+ but must be
// supplied manually for older frameworks.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
