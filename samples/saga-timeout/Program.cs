using Microsoft.Extensions.DependencyInjection;
using Mosaic;
using Mosaic.Sagas;
using Mosaic.Sample.SagaTimeout;

var services = new ServiceCollection();
services.AddLogging();
var scheduler = new InMemoryScheduler();
services.AddSingleton<IScheduledMessageStore>(scheduler);
services.AddSingleton(typeof(ISagaStateStore<>), typeof(InMemorySagaState<>));
services.AddMosaic();

await using var sp = services.BuildServiceProvider();

// Run 1 — hold expires, OrderAccepted fires.
{
    Console.WriteLine("══════ Run 1: customer waits out the hold ══════");
    var orderId = Guid.NewGuid();
    using (var scope = sp.CreateScope())
    {
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();
        await engine.Publish(new OrderPlaced(orderId, HoldSeconds: 1));
    }
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await scheduler.RunUntilEmptyAsync(sp, cts.Token);
    Console.WriteLine();
}

// Run 2 — cancel arrives during the hold, no OrderAccepted ever fires.
{
    Console.WriteLine("══════ Run 2: customer cancels mid-hold ══════");
    var orderId = Guid.NewGuid();
    using (var scope = sp.CreateScope())
    {
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();
        await engine.Publish(new OrderPlaced(orderId, HoldSeconds: 60));
        await Task.Delay(200);
        await engine.Publish(new OrderCancellationRequested(orderId, "changed_mind"));
    }
    // Saga.Complete() cleared the timeout via CancelByPrefixAsync — RunUntilEmpty has nothing to do.
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await scheduler.RunUntilEmptyAsync(sp, cts.Token);
    Console.WriteLine("[saga]      no OrderAccepted fired — hold was absorbed by the cancel");
}
