namespace Mosaic.Sample.Sales;

// Command — imperative, single handler in the owning service (Sales).
public sealed record PlaceOrder(int CustomerId, IReadOnlyList<int> ProductIds) : IRequest<int>;

// Event — published by the owning service. Other services subscribe via IEventHandler<OrderPlaced>
// in their own assemblies. The publisher knows nothing about who's listening.
public sealed record OrderPlaced(int OrderId, int CustomerId, IReadOnlyList<int> ProductIds) : IEvent;
