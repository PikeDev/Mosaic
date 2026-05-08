using System.Diagnostics;
using System.Reflection;

namespace Mosaic.Runtime;

/// <summary>
/// The shared <see cref="ActivitySource"/> used by source-generated dispatch code to emit
/// per-invocation spans for handlers, composers, event handlers, and pipeline behaviors.
/// </summary>
/// <remarks>
/// Subscribe via <c>OpenTelemetry.AddSource("Mosaic")</c> to export Mosaic spans to your
/// configured exporter.
/// </remarks>
public static class MosaicActivitySource
{
    public const string SourceName = "Mosaic";

    public static readonly ActivitySource Instance = new(
        SourceName,
        typeof(MosaicActivitySource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0");

    /// <summary>Standard tag keys emitted on Mosaic spans.</summary>
    public static class Tags
    {
        public const string MessageType = "mosaic.message.type";
        public const string MessageKind = "mosaic.message.kind";       // "request" | "composable" | "event"
        public const string HandlerType = "mosaic.handler.type";
        public const string CorrelationId = "mosaic.correlation_id";
        public const string CausationId = "mosaic.causation_id";
    }
}
