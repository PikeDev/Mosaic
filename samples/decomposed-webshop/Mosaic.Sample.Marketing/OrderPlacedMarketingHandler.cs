using Mosaic.Sample.Sales;

namespace Mosaic.Sample.Marketing;

// A second, independent subscriber to the same event — Marketing has no relationship with
// Inventory. Adding a third subscriber tomorrow is a one-class change with no edits to Sales.
public sealed class OrderPlacedMarketingHandler : IEventHandler<OrderPlaced>
{
    public ValueTask Handle(OrderPlaced notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[marketing]  order confirmation queued for customer {notification.CustomerId}");
        return default;
    }
}
