using Mosaic;
using Mosaic.Sagas;

namespace Mosaic.Sample.SagaTimeout;

// ─── Domain messages ────────────────────────────────────────────────────────────────────────────

/// <summary>The starter event. Sales raises it the moment the customer clicks "place order";
/// the saga uses the buyer-remorse window to absorb a quick mind-change before downstream side
/// effects fire.</summary>
public sealed record OrderPlaced(Guid OrderId, int HoldSeconds) : IEvent;

/// <summary>Optional cancel that the customer can issue during the hold window.</summary>
public sealed record OrderCancellationRequested(Guid OrderId, string Reason) : IEvent;

/// <summary>The timeout the saga schedules for itself; the relay calls it back when due.</summary>
public sealed record BuyersRemorseExpired(Guid OrderId) : IEvent;

/// <summary>Downstream side effect — only published once the hold window has elapsed without a cancel.</summary>
public sealed record OrderAccepted(Guid OrderId) : IEvent;

// ─── Saga state ─────────────────────────────────────────────────────────────────────────────────

public sealed class BuyersRemorseData : SagaData
{
    [Correlation]
    public Guid OrderId { get; set; }
    public int HoldSeconds { get; set; }
}

// ─── The saga itself ────────────────────────────────────────────────────────────────────────────

public sealed class BuyersRemorseSaga :
    Saga<BuyersRemorseData>,
    IStartedBy<OrderPlaced>,
    IHandles<OrderCancellationRequested>,
    IHandles<BuyersRemorseExpired>
{
    public async Task Handle(OrderPlaced message, ICompositionContext context, CancellationToken cancellationToken)
    {
        Data.HoldSeconds = message.HoldSeconds;
        Console.WriteLine($"[saga]      hold started for order {message.OrderId} ({message.HoldSeconds}s)");
        await Schedule(context, TimeSpan.FromSeconds(message.HoldSeconds), new BuyersRemorseExpired(message.OrderId), cancellationToken);
        TransitionTo(BuyersRemorseSagaState.Holding);
    }

    [During(BuyersRemorseSagaState.Holding)]
    public Task Handle(OrderCancellationRequested message, ICompositionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[saga]      cancel within hold window — saga complete, no OrderAccepted");
        Complete();
        return Task.CompletedTask;
    }

    [During(BuyersRemorseSagaState.Holding)]
    public async Task Handle(BuyersRemorseExpired message, ICompositionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[saga]      hold expired — publishing OrderAccepted");
        await context.Publish(new OrderAccepted(Data.OrderId), cancellationToken);
        Complete();
    }
}

public static class BuyersRemorseSagaState
{
    public const string Holding = nameof(Holding);
}

// ─── Downstream subscriber for OrderAccepted ───────────────────────────────────────────────────

public sealed class OrderAcceptedLogger : IEventHandler<OrderAccepted>
{
    public ValueTask Handle(OrderAccepted notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[downstream] OrderAccepted received for {notification.OrderId}");
        return default;
    }
}
