using Mosaic.Sample.Sales;

namespace Mosaic.Sample.Inventory;

// Inventory reacts to OrderPlaced — Sales doesn't know Inventory exists. The two are decoupled
// via the event; if Inventory is offline, Sales still completes (subject to pubsub durability
// guarantees, which are an infrastructure concern beyond this in-process sample).
public sealed class OrderPlacedInventoryHandler : IEventHandler<OrderPlaced>
{
    public ValueTask Handle(OrderPlaced notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        foreach (var productId in notification.ProductIds)
        {
            InventoryStore.Reserve(productId, quantity: 1);
            Console.WriteLine($"[inventory]  reserved 1 unit of product {productId}");
        }
        return default;
    }
}
