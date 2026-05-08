using Mosaic;

namespace Mosaic.Sample.AuditTrail;

// Three events forming a chain: each one's handler publishes the next.
public sealed record OrderPlaced(int OrderId) : IEvent;
public sealed record OrderAccepted(int OrderId) : IEvent;
public sealed record ShipmentArranged(int OrderId, string Carrier) : IEvent;

// First link — handler publishes OrderAccepted as a child of OrderPlaced.
public sealed class OrderPlacedHandler : IEventHandler<OrderPlaced>
{
    public async ValueTask Handle(OrderPlaced notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        await context.Publish(new OrderAccepted(notification.OrderId), cancellationToken);
    }
}

// Second link — handler publishes ShipmentArranged as a child of OrderAccepted.
public sealed class OrderAcceptedHandler : IEventHandler<OrderAccepted>
{
    public async ValueTask Handle(OrderAccepted notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        await context.Publish(new ShipmentArranged(notification.OrderId, "ACME-EXPRESS"), cancellationToken);
    }
}

// Terminal subscriber — no further publishes.
public sealed class ShipmentArrangedHandler : IEventHandler<ShipmentArranged>
{
    public ValueTask Handle(ShipmentArranged notification, ICompositionContext context, CancellationToken cancellationToken)
        => default;
}
