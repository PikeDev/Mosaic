using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Mosaic.Runtime;

/// <summary>
/// <see cref="IMosaicSerializer{T}"/> backed by a <see cref="JsonTypeInfo{T}"/> from a
/// <see cref="JsonSerializerContext"/>. AOT-clean: no reflection on the hot path. Use this
/// indirectly via <c>builder.UseSystemTextJsonContext(MyContext.Default)</c>.
/// </summary>
public sealed class SourceGeneratedJsonSerializer<T> : IMosaicSerializer<T>
{
    private readonly JsonTypeInfo<T> _typeInfo;

    public SourceGeneratedJsonSerializer(JsonTypeInfo<T> typeInfo)
    {
        System.ArgumentNullException.ThrowIfNull(typeInfo);
        _typeInfo = typeInfo;
    }

    public void Serialize(IBufferWriter<byte> writer, T value)
    {
        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, value, _typeInfo);
    }

    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length == 0) return default;
        var reader = new Utf8JsonReader(buffer);
        return JsonSerializer.Deserialize(ref reader, _typeInfo);
    }
}

/// <summary>
/// AOT-friendly <see cref="IMosaicSerializerRegistry"/> backed by a user-declared
/// <see cref="JsonSerializerContext"/>. The context's <see cref="JsonSerializableAttribute"/>
/// declarations enumerate every event type the registry can serialize; a missing entry throws
/// a clear error pointing the user at the fix.
/// <para>
/// Recommended over the reflection-based default when targeting native AOT or trimmed publish.
/// Pair with <c>UseSystemTextJsonContext</c> on the builder:
/// <code>
/// [JsonSerializable(typeof(OrderPlaced))]
/// [JsonSerializable(typeof(OrderAccepted))]
/// internal sealed partial class WebshopJsonContext : JsonSerializerContext;
///
/// services.AddMosaic().UseSystemTextJsonContext(WebshopJsonContext.Default);
/// </code>
/// </para>
/// </summary>
public sealed class SystemTextJsonContextRegistry : IMosaicSerializerRegistry
{
    private readonly JsonSerializerContext _context;
    private readonly ConcurrentDictionary<System.Type, object> _cache = new();

    public SystemTextJsonContextRegistry(JsonSerializerContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public IMosaicSerializer<T> GetSerializer<T>()
        => (IMosaicSerializer<T>)_cache.GetOrAdd(typeof(T), _ =>
        {
            // The cast happens inside the generic method, so the AOT compiler can specialise
            // — no Type.MakeGenericType / Activator.CreateInstance reflection required.
            var typeInfo = _context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
                ?? throw new System.InvalidOperationException(
                    $"JsonSerializerContext '{_context.GetType().FullName}' does not provide TypeInfo for {typeof(T).FullName}. "
                    + $"Add [JsonSerializable(typeof({typeof(T).Name}))] to your context partial class.");
            return new SourceGeneratedJsonSerializer<T>(typeInfo);
        });
}
