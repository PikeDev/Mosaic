using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Mosaic.Runtime;

/// <summary>
/// Builder extension for swapping the default reflection-based JSON registry for an AOT-friendly
/// <see cref="SystemTextJsonContextRegistry"/> backed by a user-declared
/// <see cref="JsonSerializerContext"/>.
/// </summary>
public static class SystemTextJsonContextMosaicBuilderExtensions
{
    /// <summary>
    /// Use <paramref name="context"/> as the source-generated JSON serializer source. Replaces the
    /// reflection-based default registered by <c>AddMosaic()</c>. Every event type Mosaic might
    /// publish or schedule must appear on <paramref name="context"/> via
    /// <c>[JsonSerializable(typeof(...))]</c>; the registry throws with a clear error when a type
    /// is missing.
    /// </summary>
    public static IMosaicBuilder UseSystemTextJsonContext(this IMosaicBuilder builder, JsonSerializerContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        ReplaceRegistry(builder.Services, ServiceDescriptor.Singleton<IMosaicSerializerRegistry>(new SystemTextJsonContextRegistry(context)));
        return builder;
    }

    private static void ReplaceRegistry(IServiceCollection services, ServiceDescriptor replacement)
    {
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IMosaicSerializerRegistry))
            {
                services.RemoveAt(i);
            }
        }
        services.Add(replacement);
    }
}
