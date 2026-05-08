namespace Mosaic.Sample.Sales;

// Sales is the technical authority for orders — only it mints OrderIds. Other services react to
// OrderPlaced rather than calling Sales directly (no RPC for behavior; pubsub for state diffusion).
public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrder, int>
{
    private static int _nextOrderId = 1000;

    public async ValueTask<int> Handle(
        PlaceOrder command,
        ICompositionContext context,
        CancellationToken cancellationToken)
    {
        var orderId = Interlocked.Increment(ref _nextOrderId);
        Console.WriteLine($"[sales]      order #{orderId} created for customer {command.CustomerId}");

        // Buffered by default — flushed after this handler returns. Inventory + Marketing handlers
        // will fire next, in their own services, with no coupling back to Sales.
        await context.Publish(new OrderPlaced(orderId, command.CustomerId, command.ProductIds), cancellationToken);

        return orderId;
    }
}
