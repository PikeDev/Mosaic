using System.Buffers;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Mosaic.Runtime;
using Mosaic.Serialization.MemoryPack;
using Mosaic.Transport.InMemory;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

[MemoryPackable]
public partial record MemoryPackedEvent(int OrderId, string Sku) : IEvent;

public sealed class MemoryPackedTracker
{
    private MemoryPackedEvent? _last;
    public MemoryPackedEvent? Last => _last;
    public void Capture(MemoryPackedEvent e) => _last = e;
}

public sealed class MemoryPackedHandler(MemoryPackedTracker tracker) : IEventHandler<MemoryPackedEvent>
{
    public ValueTask Handle(MemoryPackedEvent notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        tracker.Capture(notification);
        return default;
    }
}

public class MemoryPackSerializerTests
{
    [Fact]
    public void Roundtrip_through_MemoryPack_serializer_preserves_payload()
    {
        var registry = new MemoryPackMosaicSerializerRegistry();
        var serializer = registry.GetSerializer<MemoryPackedEvent>();

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, new MemoryPackedEvent(7, "SKU-7"));

        // Confirm it is binary, NOT JSON — first byte of MemoryPack is never '{'.
        writer.WrittenSpan[0].ShouldNotBe((byte)'{');

        var roundtripped = serializer.Deserialize(new ReadOnlySequence<byte>(writer.WrittenMemory));
        roundtripped.ShouldNotBeNull();
        roundtripped!.OrderId.ShouldBe(7);
        roundtripped.Sku.ShouldBe("SKU-7");
    }

    [Fact]
    public async Task Engine_uses_MemoryPack_when_registry_is_swapped()
    {
        // Register the MemoryPack registry BEFORE AddMosaic so the source-generated
        // TryAddSingleton<IMosaicSerializerRegistry, Default> is a no-op. Cross-container
        // dispatch via the in-memory transport forces a real Serialize → bytes → Deserialize trip,
        // proving the abstraction actually flows through the engine.
        var publisher = new ServiceCollection()
            .AddSingleton<MemoryPackedTracker>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IMosaicSerializerRegistry, MemoryPackMosaicSerializerRegistry>()
            .AddMosaic().UseInMemoryTransport("mempack-test").Services
            .BuildServiceProvider();

        var subscriber = new ServiceCollection()
            .AddSingleton<MemoryPackedTracker>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IMosaicSerializerRegistry, MemoryPackMosaicSerializerRegistry>()
            .AddMosaic().UseInMemoryTransport("mempack-test").Services
            .BuildServiceProvider();

        _ = publisher.GetRequiredService<IEventTransport>();
        _ = subscriber.GetRequiredService<IEventTransport>();

        publisher.GetRequiredService<IMosaicSerializerRegistry>()
            .ShouldBeOfType<MemoryPackMosaicSerializerRegistry>();

        using (var scope = publisher.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();
            await engine.Publish(new MemoryPackedEvent(123, "SKU-MP"));
        }

        subscriber.GetRequiredService<MemoryPackedTracker>().Last.ShouldNotBeNull();
        subscriber.GetRequiredService<MemoryPackedTracker>().Last!.OrderId.ShouldBe(123);
        subscriber.GetRequiredService<MemoryPackedTracker>().Last!.Sku.ShouldBe("SKU-MP");

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }
}
