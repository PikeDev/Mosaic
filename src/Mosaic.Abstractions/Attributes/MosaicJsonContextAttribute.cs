namespace Mosaic;

/// <summary>
/// Opts a <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>-derived class into
/// Mosaic's coverage analyzer. The analyzer warns (MOSAIC0006) when the context is missing a
/// <c>[JsonSerializable(typeof(X))]</c> for any <c>IEvent</c> the source generator has discovered
/// in the compilation — keeping the AOT-friendly JSON registry in sync with the event catalog
/// without manual auditing.
/// </summary>
/// <example>
/// <code>
/// [MosaicJsonContext]
/// [JsonSerializable(typeof(OrderPlaced))]
/// [JsonSerializable(typeof(OrderAccepted))]
/// internal sealed partial class WebshopJsonContext : JsonSerializerContext;
///
/// services.AddMosaic().UseSystemTextJsonContext(WebshopJsonContext.Default);
/// </code>
/// </example>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MosaicJsonContextAttribute : System.Attribute;
