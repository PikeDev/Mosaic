using System.Buffers;

namespace Mosaic.Serialization.MemoryPack;

/// <summary>
/// <see cref="IMosaicSerializer{T}"/> backed by Cysharp.MemoryPack — a high-throughput binary
/// serializer with source-gen support. Both <see cref="Serialize"/> and <see cref="Deserialize"/>
/// are zero-allocation when the message type is registered as <c>[MemoryPackable] partial</c>.
/// <para>
/// Caller responsibility: every type that flows through this serializer must be annotated
/// <c>[MemoryPackable]</c> (or <c>[MemoryPackUnion]</c>) and declared <c>partial</c> so MemoryPack's
/// own source generator can emit the formatter. Types that lack the annotation throw at runtime
/// when first dispatched — fall back to the default JSON registry for those.
/// </para>
/// </summary>
public sealed class MemoryPackMosaicSerializer<T> : IMosaicSerializer<T>
{
    public static readonly MemoryPackMosaicSerializer<T> Default = new();

    public void Serialize(IBufferWriter<byte> writer, T value)
        => global::MemoryPack.MemoryPackSerializer.Serialize(writer, value);

    public T? Deserialize(in ReadOnlySequence<byte> buffer)
        => global::MemoryPack.MemoryPackSerializer.Deserialize<T>(buffer);
}

/// <summary>
/// MemoryPack-backed <see cref="IMosaicSerializerRegistry"/>. Returns the singleton
/// <see cref="MemoryPackMosaicSerializer{T}.Default"/> for every requested type.
/// <para>
/// Wire it BEFORE <c>AddMosaic()</c> so the source-generated registration's <c>TryAddSingleton</c>
/// for the JSON default is a no-op:
/// <code>
/// services.AddSingleton&lt;IMosaicSerializerRegistry, MemoryPackMosaicSerializerRegistry&gt;();
/// services.AddMosaic();
/// </code>
/// </para>
/// </summary>
public sealed class MemoryPackMosaicSerializerRegistry : IMosaicSerializerRegistry
{
    public IMosaicSerializer<T> GetSerializer<T>() => MemoryPackMosaicSerializer<T>.Default;
}
