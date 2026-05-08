using Microsoft.Extensions.DependencyInjection;
using Mosaic;
using Mosaic.Testing;
using Shouldly;
using Xunit;

namespace Mosaic.Tests.HarnessFixtures;

// ─── Fixtures ──────────────────────────────────────────────────────────────────────────────────

public sealed record GetGreeting(string Name) : IRequest<string>;

public sealed class GreetingHandler : IRequestHandler<GetGreeting, string>
{
    public ValueTask<string> Handle(GetGreeting r, ICompositionContext context, CancellationToken cancellationToken)
        => new($"Hello, {r.Name}");
}

public sealed record OrderPlaced(int OrderId) : IEvent;
public sealed record OrderAccepted(int OrderId) : IEvent;

public sealed class AcceptOnPlaced : IEventHandler<OrderPlaced>
{
    public async ValueTask Handle(OrderPlaced e, ICompositionContext context, CancellationToken cancellationToken)
    {
        await context.Publish(new OrderAccepted(e.OrderId), cancellationToken);
    }
}

// Stub: required so the source generator emits a Publish_OrderAccepted dispatch case. Events
// without any IEventHandler are silent no-ops at the dispatch level — including a no-op handler
// keeps the test realistic (most published events have at least one downstream subscriber).
public sealed class NoOpAcceptedHandler : IEventHandler<OrderAccepted>
{
    public ValueTask Handle(OrderAccepted notification, ICompositionContext context, CancellationToken cancellationToken)
        => default;
}

public sealed class TileVm
{
    public int ProductId { get; set; }
    public string Title { get; set; } = "";
    public decimal UnitPrice { get; set; }
}

public sealed record GetTile(int ProductId) : IComposable<TileVm>;

public sealed class TitleComposer : IComposer<GetTile, TileVm>
{
    public ValueTask Compose(GetTile r, TileVm vm, ICompositionContext context, CancellationToken cancellationToken)
    {
        vm.ProductId = r.ProductId;
        vm.Title = $"Product #{r.ProductId}";
        return default;
    }
}

public sealed class PriceComposer : IComposer<GetTile, TileVm>
{
    public ValueTask Compose(GetTile r, TileVm vm, ICompositionContext context, CancellationToken cancellationToken)
    {
        vm.UnitPrice = 19.99m;
        return default;
    }
}

// ─── Tests ──────────────────────────────────────────────────────────────────────────────────────

public class TestHarnessTests
{
    [Fact]
    public async Task Records_sent_request()
    {
        await using var harness = await MosaicTestHarness.CreateAsync(s =>
        {
            s.AddMosaicTestHarness();
            s.AddMosaic();
        });

        var greeting = await harness.Engine.Send(new GetGreeting("world"));
        greeting.ShouldBe("Hello, world");

        harness.Sent<GetGreeting>().Count.ShouldBe(1);
        harness.Sent<GetGreeting>().All[0].Name.ShouldBe("world");
    }

    [Fact]
    public async Task Records_published_events_including_handler_chain()
    {
        await using var harness = await MosaicTestHarness.CreateAsync(s =>
        {
            s.AddMosaicTestHarness();
            s.AddMosaic();
        });

        await harness.Engine.Publish(new OrderPlaced(42));

        // Original publish + handler-chained publish both get recorded.
        harness.Published<OrderPlaced>().Count.ShouldBe(1);
        var accepted = await harness.Published<OrderAccepted>().WaitForAsync(count: 1);
        accepted[0].OrderId.ShouldBe(42);
    }

    [Fact]
    public async Task Records_composed_view_model_after_chain()
    {
        await using var harness = await MosaicTestHarness.CreateAsync(s =>
        {
            s.AddMosaicTestHarness();
            s.AddMosaic();
        });

        var tile = await harness.Engine.Compose(new GetTile(7));

        tile.Title.ShouldBe("Product #7");
        tile.UnitPrice.ShouldBe(19.99m);

        harness.Composed<GetTile>().Count.ShouldBe(1);
        var resultVms = harness.ComposedResults<TileVm>().All;
        resultVms.Count.ShouldBe(1);
        resultVms[0].ProductId.ShouldBe(7);
    }

    [Fact]
    public async Task WaitForAsync_throws_on_timeout()
    {
        await using var harness = await MosaicTestHarness.CreateAsync(s =>
        {
            s.AddMosaicTestHarness();
            s.AddMosaic();
        });

        var ex = await Should.ThrowAsync<TimeoutException>(async () =>
            await harness.Published<OrderAccepted>().WaitForAsync(count: 1, timeout: TimeSpan.FromMilliseconds(150)));

        ex.Message.ShouldContain("OrderAccepted");
    }
}
