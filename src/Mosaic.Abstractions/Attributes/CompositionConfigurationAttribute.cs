using Microsoft.Extensions.DependencyInjection;

namespace Mosaic;

/// <summary>
/// Configures the source generator for the assembly it's applied to (typically the composition root).
/// Applied with the <c>[assembly: ...]</c> target.
/// </summary>
/// <example>
/// <code>
/// [assembly: CompositionConfiguration(
///     DefaultLifetime = ServiceLifetime.Scoped,
///     EventPublishMode = EventPublishMode.Buffered,
///     PipelineBehaviors = new[] {
///         typeof(LoggingBehavior&lt;,&gt;),
///         typeof(ValidationBehavior&lt;,&gt;),
///         typeof(TransactionBehavior&lt;,&gt;)
///     })]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class CompositionConfigurationAttribute : Attribute
{
    /// <summary>Default DI lifetime for handlers, composers, event handlers, and behaviors.</summary>
    public ServiceLifetime DefaultLifetime { get; init; } = ServiceLifetime.Scoped;

    /// <summary>How events raised inside a composition are flushed.</summary>
    public EventPublishMode EventPublishMode { get; init; } = EventPublishMode.Buffered;

    /// <summary>
    /// Open-generic pipeline-behavior types in execution order (outermost first). Each type must
    /// implement <see cref="IPipelineBehavior{TRequest, TResponse}"/>. Wraps every <c>Send</c>.
    /// </summary>
    public Type[] PipelineBehaviors { get; init; } = [];

    /// <summary>
    /// Open-generic publish-behavior types in execution order (outermost first). Each type must
    /// implement <see cref="IPublishBehavior{TEvent}"/>. Wraps every <c>Publish</c> once per event
    /// (around the entire handler fan-out, not per individual handler).
    /// </summary>
    public Type[] PublishBehaviors { get; init; } = [];

    /// <summary>
    /// Open-generic compose-behavior types in execution order (outermost first). Each type must
    /// implement <see cref="IComposeBehavior{TRequest, TViewModel}"/>. Wraps every <c>Compose</c>
    /// call once (around the entire composer fan-out).
    /// </summary>
    public Type[] ComposeBehaviors { get; init; } = [];

    /// <summary>
    /// Whether the generator should emit <see cref="System.Diagnostics.Activity"/> spans wrapping
    /// every handler / composer / event-handler invocation. Defaults to true.
    /// </summary>
    public bool EmitTelemetry { get; init; } = true;

    /// <summary>
    /// Namespace into which the generator emits its types. Defaults to
    /// <c>{RootNamespace}.Generated.Mosaic</c>.
    /// </summary>
    public string? GeneratedNamespace { get; init; }
}

/// <summary>How an event raised inside a composition is dispatched.</summary>
public enum EventPublishMode
{
    /// <summary>Events buffer on the context and flush after the parent composition completes (outbox-style; default).</summary>
    Buffered = 0,

    /// <summary>Events dispatch immediately on raise; handlers may race the parent composition.</summary>
    Eager = 1,
}
