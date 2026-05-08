using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

public sealed record GetGreeting(string Name) : IRequest<string>;

public sealed class GreetingHandler : IRequestHandler<GetGreeting, string>
{
    public ValueTask<string> Handle(GetGreeting request, ICompositionContext context, CancellationToken cancellationToken)
        => new($"Hello, {request.Name}");
}

public sealed record OrderSummary
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string CustomerName { get; set; } = "";
}

public sealed record GetOrderSummary(int OrderId) : IComposable<OrderSummary>;

public sealed class OrderTotalsComposer : IComposer<GetOrderSummary, OrderSummary>
{
    public ValueTask Compose(GetOrderSummary request, OrderSummary viewModel, ICompositionContext context, CancellationToken cancellationToken)
    {
        viewModel.OrderId = request.OrderId;
        viewModel.Total = 42.50m;
        return default;
    }
}

public sealed class OrderCustomerComposer : IComposer<GetOrderSummary, OrderSummary>
{
    public ValueTask Compose(GetOrderSummary request, OrderSummary viewModel, ICompositionContext context, CancellationToken cancellationToken)
    {
        viewModel.CustomerName = "Ada Lovelace";
        return default;
    }
}

public sealed record OrderPlaced(int OrderId) : IEvent;

public sealed class OrderPlacedAuditor : IEventHandler<OrderPlaced>
{
    private static int _callCount;
    public static int CallCount => _callCount;
    public static void Reset() => Interlocked.Exchange(ref _callCount, 0);

    public ValueTask Handle(OrderPlaced notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return default;
    }
}

public sealed record SendOrderPlacedRequest(int OrderId) : IRequest<int>;

public sealed class SendOrderPlacedHandler : IRequestHandler<SendOrderPlacedRequest, int>
{
    public async ValueTask<int> Handle(SendOrderPlacedRequest request, ICompositionContext context, CancellationToken cancellationToken)
    {
        await context.Publish(new OrderPlaced(request.OrderId), cancellationToken);
        return request.OrderId;
    }
}

// Scoped probe + two handlers that capture the probe's instance id. Used to prove parallel
// event handlers each get their own DI sub-scope (two ids, not one shared id).
public sealed class ScopeProbe
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}

public sealed record ScopedEvent : IEvent;

public sealed class FirstScopedHandler(ScopeProbe probe) : IEventHandler<ScopedEvent>
{
    public static Guid LastSeenId { get; set; }
    public ValueTask Handle(ScopedEvent notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        LastSeenId = probe.InstanceId;
        return default;
    }
}

public sealed class SecondScopedHandler(ScopeProbe probe) : IEventHandler<ScopedEvent>
{
    public static Guid LastSeenId { get; set; }
    public ValueTask Handle(ScopedEvent notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        LastSeenId = probe.InstanceId;
        return default;
    }
}

public class EndToEndDispatchTests
{
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddMosaic();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_dispatches_to_request_handler()
    {
        await using var sp = BuildContainer();
        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();

        var greeting = await engine.Send(new GetGreeting("World"));

        greeting.ShouldBe("Hello, World");
    }

    [Fact]
    public async Task Compose_runs_all_composers_for_view_model()
    {
        await using var sp = BuildContainer();
        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();

        var summary = await engine.Compose(new GetOrderSummary(7));

        summary.OrderId.ShouldBe(7);
        summary.Total.ShouldBe(42.50m);
        summary.CustomerName.ShouldBe("Ada Lovelace");
    }

    [Fact]
    public async Task Raise_during_request_publishes_after_handler_completes()
    {
        OrderPlacedAuditor.Reset();
        await using var sp = BuildContainer();
        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();

        var orderId = await engine.Send(new SendOrderPlacedRequest(99));

        orderId.ShouldBe(99);
        OrderPlacedAuditor.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_event_handlers_each_resolve_in_their_own_di_scope()
    {
        // Each of the two ScopedEvent handlers injects a scoped ScopeProbe. Without per-handler
        // scoping they'd share the parent request scope and therefore the same probe instance;
        // with per-handler scoping (the source-gen rewrite) they each get their own.
        FirstScopedHandler.LastSeenId = Guid.Empty;
        SecondScopedHandler.LastSeenId = Guid.Empty;

        var services = new ServiceCollection();
        services.AddMosaic();
        services.AddScoped<ScopeProbe>();
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();

        await engine.Publish(new ScopedEvent());

        FirstScopedHandler.LastSeenId.ShouldNotBe(Guid.Empty);
        SecondScopedHandler.LastSeenId.ShouldNotBe(Guid.Empty);
        FirstScopedHandler.LastSeenId.ShouldNotBe(SecondScopedHandler.LastSeenId);
    }
}
