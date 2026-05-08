using System.Buffers;

namespace Mosaic;

/// <summary>
/// Serializer for an outbound message of <typeparamref name="T"/>. Writes into a caller-supplied
/// <see cref="IBufferWriter{Byte}"/> so the caller controls buffer pooling — the engine writes the
/// serialised payload then hands the resulting <see cref="ReadOnlySequence{Byte}"/> straight to
/// the transport without an extra copy.
/// </summary>
/// <typeparam name="T">The CLR shape of the message being serialised.</typeparam>
public interface IMosaicSerialize<in T>
{
    /// <summary>Serialise <paramref name="value"/> into <paramref name="writer"/>.</summary>
    void Serialize(IBufferWriter<byte> writer, T value);
}

/// <summary>
/// Deserializer for an inbound message of <typeparamref name="T"/>. Reads from a
/// <see cref="ReadOnlySequence{Byte}"/> so transports that deliver multi-segment payloads
/// (Postgres NOTIFY chunks, async pipes, etc.) avoid a contiguous-buffer copy.
/// </summary>
/// <typeparam name="T">The CLR shape to materialise.</typeparam>
public interface IMosaicDeserialize<out T>
{
    /// <summary>Deserialise <paramref name="buffer"/> into a <typeparamref name="T"/>; returns
    /// default when the buffer is empty.</summary>
    T? Deserialize(in ReadOnlySequence<byte> buffer);
}

/// <summary>
/// Combined serializer + deserializer for <typeparamref name="T"/>. The default implementation
/// (<c>SystemTextJsonMosaicSerializer&lt;T&gt;</c>) ships with Mosaic.Runtime; alternative
/// implementations (MemoryPack, MessagePack, Protobuf, hand-rolled) plug in via
/// <see cref="IMosaicSerializerRegistry"/>.
/// </summary>
public interface IMosaicSerializer<T> : IMosaicSerialize<T>, IMosaicDeserialize<T>;

/// <summary>
/// Resolves the <see cref="IMosaicSerializer{T}"/> to use for a given message type. Registered as
/// a singleton; the engine consults it once per dispatch site (the source-gen-emitted dispatcher
/// closes over the per-T resolution at the call site, so there's no per-message dictionary lookup
/// in the hot path).
/// </summary>
public interface IMosaicSerializerRegistry
{
    /// <summary>Returns the serializer for <typeparamref name="T"/>.</summary>
    IMosaicSerializer<T> GetSerializer<T>();
}
