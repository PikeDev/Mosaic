using System.Buffers;
using Mosaic.Runtime;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

public class MosaicBufferWriterTests
{
    [Fact]
    public void Rent_write_read_roundtrip()
    {
        using var writer = MosaicBufferWriter.Rent();
        var span = writer.GetSpan(4);
        span[0] = 1; span[1] = 2; span[2] = 3; span[3] = 4;
        writer.Advance(4);

        writer.WrittenCount.ShouldBe(4);
        writer.WrittenSpan.ToArray().ShouldBe(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void Grow_path_preserves_existing_contents_across_pool_swap()
    {
        // Start tiny so we force several grow cycles, each rent-copy-return.
        using var writer = MosaicBufferWriter.Rent(initialCapacity: 8);
        const int total = 5_000;
        for (var i = 0; i < total; i++)
        {
            writer.GetSpan(1)[0] = (byte)(i & 0xFF);
            writer.Advance(1);
        }

        writer.WrittenCount.ShouldBe(total);
        var data = writer.WrittenSpan.ToArray();
        data[0].ShouldBe((byte)0);
        data[255].ShouldBe((byte)255);
        data[256].ShouldBe((byte)0);
        data[total - 1].ShouldBe((byte)((total - 1) & 0xFF));
    }

    [Fact]
    public void Dispose_returns_array_and_makes_writer_unusable()
    {
        var writer = MosaicBufferWriter.Rent();
        writer.GetSpan(1)[0] = 0xAB;
        writer.Advance(1);
        writer.Dispose();

        Should.Throw<ObjectDisposedException>(() => _ = writer.WrittenMemory);
        Should.Throw<ObjectDisposedException>(() => writer.GetSpan());

        // Double-dispose is a no-op.
        writer.Dispose();
    }

    [Fact]
    public void IBufferWriter_contract_grows_when_size_hint_exceeds_remaining_capacity()
    {
        using var writer = MosaicBufferWriter.Rent(initialCapacity: 16);

        var first = writer.GetSpan(4);
        for (var i = 0; i < 4; i++) first[i] = (byte)(0xA0 + i);
        writer.Advance(4);

        // Ask for more than remaining capacity in a single hint — must trigger Grow().
        var second = writer.GetSpan(64);
        second.Length.ShouldBeGreaterThanOrEqualTo(64);
        for (var i = 0; i < 64; i++) second[i] = (byte)i;
        writer.Advance(64);

        writer.WrittenCount.ShouldBe(68);
        var data = writer.WrittenSpan.ToArray();
        data[0].ShouldBe((byte)0xA0);
        data[3].ShouldBe((byte)0xA3);
        data[4].ShouldBe((byte)0);
        data[67].ShouldBe((byte)63);
    }

    [Fact]
    public void Implements_IBufferWriter_for_serializer_use()
    {
        using var writer = MosaicBufferWriter.Rent();
        IBufferWriter<byte> asInterface = writer;
        asInterface.Write(new byte[] { 9, 8, 7 });
        writer.WrittenCount.ShouldBe(3);
    }
}
