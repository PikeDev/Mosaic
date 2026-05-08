using System.Buffers;

namespace Mosaic.Runtime;

/// <summary>
/// Heap-pooled <see cref="IBufferWriter{T}"/> over <c>byte</c>, rented from
/// <see cref="ArrayPool{T}.Shared"/>. Replaces <see cref="ArrayBufferWriter{T}"/> on the hot
/// publish path so each event publish doesn't allocate a fresh backing array.
/// <para>
/// Lifetime: rent → write → hand <see cref="WrittenMemory"/> as a <c>ReadOnlySequence&lt;byte&gt;</c>
/// to the transport → <see cref="Dispose"/> returns the array to the pool. The transport must
/// fully consume (copy or send) the payload before <see cref="Dispose"/> runs — every Mosaic
/// transport copies into its own buffer before <c>PublishAsync</c> awaits, so the source
/// generator can safely emit <c>using var __writer = MosaicBufferWriter.Rent();</c>.
/// </para>
/// <para>
/// Adapted from CommunityToolkit's <c>ArrayPoolBufferWriter</c> (also used by nats.net's
/// <c>NatsBufferWriter</c>) — same growth policy, scoped to <c>byte</c> for our use.
/// </para>
/// </summary>
public sealed class MosaicBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int DefaultInitialCapacity = 256;

    private byte[]? _array;
    private int _written;

    private MosaicBufferWriter(int initialCapacity)
    {
        _array = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _written = 0;
    }

    /// <summary>Rent a writer with the default 256-byte initial capacity.</summary>
    public static MosaicBufferWriter Rent() => new(DefaultInitialCapacity);

    /// <summary>Rent a writer sized to fit at least <paramref name="initialCapacity"/> bytes.</summary>
    public static MosaicBufferWriter Rent(int initialCapacity) => new(initialCapacity);

    public ReadOnlyMemory<byte> WrittenMemory
        => (_array ?? ThrowDisposed()).AsMemory(0, _written);

    public ReadOnlySpan<byte> WrittenSpan
        => (_array ?? ThrowDisposed()).AsSpan(0, _written);

    public int WrittenCount => _written;

    public void Advance(int count)
    {
        var array = _array ?? ThrowDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_written > array.Length - count) throw new ArgumentException("Advanced past the end of the buffer.", nameof(count));
        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _array!.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _array!.AsSpan(_written);
    }

    public void Dispose()
    {
        var array = _array;
        if (array is null) return;
        _array = null;
        // clearArray:false — payload is plain bytes, no rooted refs to clear.
        ArrayPool<byte>.Shared.Return(array);
    }

    private void EnsureCapacity(int sizeHint)
    {
        var array = _array ?? ThrowDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        if (sizeHint == 0) sizeHint = 1;
        if (sizeHint > array.Length - _written) Grow(sizeHint);
    }

    private void Grow(int sizeHint)
    {
        var oldArray = _array!;
        var minimumSize = checked(_written + sizeHint);
        // Above ArrayPool's pooled threshold (1MB) the pool just allocates exactly, so round to
        // the next power of two to keep amortised growth linear instead of resizing every write.
        if (minimumSize > 1024 * 1024) minimumSize = RoundUpToPowerOf2(minimumSize);

        var newArray = ArrayPool<byte>.Shared.Rent(minimumSize);
        Array.Copy(oldArray, 0, newArray, 0, _written);
        _array = newArray;
        ArrayPool<byte>.Shared.Return(oldArray);
    }

    private static int RoundUpToPowerOf2(int value)
    {
        // Branch-free if value is already a power of 2 we still go up — fine for grow path.
        var v = (uint)value - 1;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return (int)(v + 1);
    }

    private static byte[] ThrowDisposed() => throw new ObjectDisposedException(nameof(MosaicBufferWriter));
}
