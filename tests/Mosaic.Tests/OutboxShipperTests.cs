using Microsoft.Extensions.DependencyInjection;
using Mosaic.Runtime;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

// Stand-in for a custom transport (NATS, RabbitMQ, etc.) — the whole point of the IOutboxShipper
// abstraction is that consumers can swap the wire without forking Mosaic.
public sealed class FakeOutboxShipper : IOutboxShipper
{
    private readonly List<OutboxShipment> _shipped = new();
    public IReadOnlyList<OutboxShipment> Shipped => _shipped;
    public ValueTask ShipBatchAsync(IReadOnlyList<OutboxShipment> batch, CancellationToken cancellationToken)
    {
        _shipped.AddRange(batch);
        return default;
    }
}

public class OutboxShipperTests
{
    [Fact]
    public void Default_AddMosaic_does_not_register_an_IOutboxShipper()
    {
        // The shipper is opt-in via a transport package. Bare AddMosaic stays minimal.
        var services = new ServiceCollection();
        services.AddMosaic();
        services.Any(s => s.ServiceType == typeof(IOutboxShipper)).ShouldBeFalse();
    }

    [Fact]
    public void Custom_IOutboxShipper_can_be_registered_directly_for_alternative_transports()
    {
        // Proves the abstraction is open for extension — a user who builds a NATS adapter
        // just registers their own IOutboxShipper before any outbox package extension runs.
        var fake = new FakeOutboxShipper();
        var sp = new ServiceCollection()
            .AddMosaic()
            .Services
            .AddSingleton<IOutboxShipper>(fake)
            .BuildServiceProvider();

        sp.GetRequiredService<IOutboxShipper>().ShouldBeSameAs(fake);
    }

    [Fact]
    public async Task FakeShipper_round_trips_an_OutboxShipment()
    {
        var fake = new FakeOutboxShipper();
        var headers = new MessageHeaders(Guid.NewGuid(), "corr-123", null, DateTime.UtcNow);
        var batch = new[]
        {
            new OutboxShipment("Sample.OrderPlaced", "sender-A", headers, new byte[] { 1, 2, 3 }),
        };

        await fake.ShipBatchAsync(batch, CancellationToken.None);

        fake.Shipped.Count.ShouldBe(1);
        fake.Shipped[0].TypeFullName.ShouldBe("Sample.OrderPlaced");
        fake.Shipped[0].Sender.ShouldBe("sender-A");
        fake.Shipped[0].Headers.CorrelationId.ShouldBe("corr-123");
        fake.Shipped[0].Payload.ToArray().ShouldBe(new byte[] { 1, 2, 3 });
    }
}
