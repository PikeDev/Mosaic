using Microsoft.Extensions.DependencyInjection;

namespace Mosaic;

/// <summary>
/// Returned by <c>AddMosaic()</c>. Mosaic-specific configuration (transports, behaviors, …) hangs
/// off this builder via chained extension methods so the call site reads as one Mosaic block:
/// <code>services.AddMosaic().UsePostgresTransport(connectionString);</code>
/// rather than as a scattered set of unrelated <see cref="IServiceCollection"/> extensions.
/// </summary>
public interface IMosaicBuilder
{
    /// <summary>The underlying service collection. Extension methods register against this.</summary>
    IServiceCollection Services { get; }
}

/// <summary>Default <see cref="IMosaicBuilder"/> implementation. Constructed by the generated <c>AddMosaic()</c>.</summary>
public sealed class MosaicBuilder(IServiceCollection services) : IMosaicBuilder
{
    public IServiceCollection Services { get; } = services;
}
