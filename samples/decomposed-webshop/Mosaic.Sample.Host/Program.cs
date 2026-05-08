using Microsoft.Extensions.DependencyInjection;
using Mosaic;
using Mosaic.Sample.Composition;
using Mosaic.Sample.Sales;

// In a real app this Host would be ASP.NET Core and each block below would be an HTTP endpoint.
// AddMosaic is source-generated — it walks every transitively-referenced sample assembly,
// discovers the handlers/composers/event handlers, and registers them with the DI container.
var services = new ServiceCollection();
services.AddMosaic();

await using var serviceProvider = services.BuildServiceProvider();

// ─── Simulated GET /checkout/summary?customerId=1&products=42,17,99 ─────────────────────────────
{
    Console.WriteLine("══════ GET /checkout/summary?customerId=1&products=42,17,99 ══════");
    using var requestScope = serviceProvider.CreateScope();
    var engine = requestScope.ServiceProvider.GetRequiredService<ICompositionEngine>();

    // Send dispatches the query to its single handler (GetCheckoutSummaryHandler in the
    // Composition assembly). That handler injects ICompositionEngine and orchestrates per line:
    // engine.Compose(GetLineItem) fans out across Catalog/Pricing/Inventory composers in parallel.
    var summary = await engine.Send(new GetCheckoutSummary(CustomerId: 1, ProductIds: [42, 17, 99]));

    Console.WriteLine();
    Console.WriteLine($"Customer #{summary.CustomerId}");
    foreach (var line in summary.Lines)
    {
        Console.WriteLine($"  product #{line.ProductId,-3}  {line.Title,-18}  {line.UnitPrice,8:C}   in-stock={line.InStock,-5} ({line.AvailableUnits} units)");
    }
    Console.WriteLine($"  TOTAL: {summary.Total:C}");
    Console.WriteLine();
}

// ─── Simulated POST /orders { customerId: 1, products: [42, 17] } ───────────────────────────────
{
    Console.WriteLine("══════ POST /orders { customerId: 1, products: [42, 17] } ══════");
    using var requestScope = serviceProvider.CreateScope();
    var engine = requestScope.ServiceProvider.GetRequiredService<ICompositionEngine>();

    // PlaceOrderHandler publishes OrderPlaced via context.Publish. Buffered events flush
    // after the handler returns — the [inventory] and [marketing] lines below come from
    // their respective IEventHandler<OrderPlaced> implementations in those assemblies,
    // with no edits ever needed in the Sales project to add new subscribers.
    var orderId = await engine.Send(new PlaceOrder(CustomerId: 1, ProductIds: [42, 17]));

    Console.WriteLine($"  → 201 Created, order #{orderId}");
}
