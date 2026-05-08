using System.Buffers;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mosaic.Runtime;
using Mosaic.Transport.InMemory;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

[MemoryPack.MemoryPackable]
public partial record JsonContextEvent(int OrderId, string Sku) : IEvent;

public sealed class JsonContextEventTracker
{
    private JsonContextEvent? _last;
    public JsonContextEvent? Last => _last;
    public void Capture(JsonContextEvent e) => _last = e;
}

public sealed class JsonContextEventHandler(JsonContextEventTracker tracker) : IEventHandler<JsonContextEvent>
{
    public ValueTask Handle(JsonContextEvent notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        tracker.Capture(notification);
        return default;
    }
}

// User-declared JsonSerializerContext — this is the file the consumer maintains. The AOT registry
// resolves type info from it, so AOT/trim-publish doesn't need reflection-based JsonSerializer paths.
[JsonSerializable(typeof(JsonContextEvent))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

public class SystemTextJsonContextRegistryTests
{
    [Fact]
    public void Registry_throws_with_actionable_error_when_type_is_not_in_context()
    {
        var registry = new SystemTextJsonContextRegistry(TestJsonContext.Default);

        // FlakyEvent is not declared on TestJsonContext — error message points at the fix.
        var ex = Should.Throw<InvalidOperationException>(() => registry.GetSerializer<FlakyEvent>());
        ex.Message.ShouldContain("FlakyEvent");
        ex.Message.ShouldContain("[JsonSerializable");
    }

    [Fact]
    public void Roundtrip_through_source_generated_serializer_preserves_payload()
    {
        var registry = new SystemTextJsonContextRegistry(TestJsonContext.Default);
        var serializer = registry.GetSerializer<JsonContextEvent>();

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, new JsonContextEvent(101, "AOT-1"));

        // First byte is JSON '{' — confirms we're writing JSON via the source-gen path.
        writer.WrittenSpan[0].ShouldBe((byte)'{');

        var roundtripped = serializer.Deserialize(new ReadOnlySequence<byte>(writer.WrittenMemory));
        roundtripped.ShouldNotBeNull();
        roundtripped!.OrderId.ShouldBe(101);
        roundtripped.Sku.ShouldBe("AOT-1");
    }

    [Fact]
    public async Task Engine_uses_source_generated_context_when_UseSystemTextJsonContext_is_called()
    {
        // Cross-container dispatch via the in-memory transport forces a real Serialize → bytes →
        // Deserialize trip, proving the AOT-friendly registry flows through the engine.
        var publisher = new ServiceCollection()
            .AddSingleton<JsonContextEventTracker>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic()
                .UseSystemTextJsonContext(TestJsonContext.Default)
                .UseInMemoryTransport("aot-json-test").Services
            .BuildServiceProvider();

        var subscriber = new ServiceCollection()
            .AddSingleton<JsonContextEventTracker>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic()
                .UseSystemTextJsonContext(TestJsonContext.Default)
                .UseInMemoryTransport("aot-json-test").Services
            .BuildServiceProvider();

        _ = publisher.GetRequiredService<IEventTransport>();
        _ = subscriber.GetRequiredService<IEventTransport>();

        publisher.GetRequiredService<IMosaicSerializerRegistry>()
            .ShouldBeOfType<SystemTextJsonContextRegistry>();

        using (var scope = publisher.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();
            await engine.Publish(new JsonContextEvent(202, "AOT-XC"));
        }

        subscriber.GetRequiredService<JsonContextEventTracker>().Last.ShouldNotBeNull();
        subscriber.GetRequiredService<JsonContextEventTracker>().Last!.OrderId.ShouldBe(202);
        subscriber.GetRequiredService<JsonContextEventTracker>().Last!.Sku.ShouldBe("AOT-XC");

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }
}
