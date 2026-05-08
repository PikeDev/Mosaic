using System.Buffers;
using System.Text.Json;

namespace Mosaic.Runtime;

/// <summary>
/// Default <see cref="IMosaicSerializer{T}"/> backed by <see cref="JsonSerializer"/>. Reads via
/// <see cref="Utf8JsonReader"/> over a <see cref="ReadOnlySequence{Byte}"/> (ref struct, no
/// allocation) and writes via <see cref="Utf8JsonWriter"/> directly into the caller's buffer
/// writer — no intermediate byte[] copy on the hot path.
/// </summary>
/// <typeparam name="T">Message shape.</typeparam>
public sealed class SystemTextJsonMosaicSerializer<T> : IMosaicSerializer<T>
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonMosaicSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public void Serialize(IBufferWriter<byte> writer, T value)
    {
        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, value, _options);
    }

    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length == 0) return default;
        var reader = new Utf8JsonReader(buffer);
        return JsonSerializer.Deserialize<T>(ref reader, _options);
    }
}

/// <summary>
/// Default <see cref="IMosaicSerializerRegistry"/> — returns a
/// <see cref="SystemTextJsonMosaicSerializer{T}"/> for every type, sharing the same
/// <see cref="JsonSerializerOptions"/>. Cached per type so the registry's cost is amortised.
/// </summary>
public sealed class DefaultMosaicSerializerRegistry : IMosaicSerializerRegistry
{
    private readonly JsonSerializerOptions _options;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, object> _cache = new();

    public DefaultMosaicSerializerRegistry(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public IMosaicSerializer<T> GetSerializer<T>()
        => (IMosaicSerializer<T>)_cache.GetOrAdd(typeof(T), _ => new SystemTextJsonMosaicSerializer<T>(_options));
}
