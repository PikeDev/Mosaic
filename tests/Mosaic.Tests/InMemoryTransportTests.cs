using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Mosaic.Runtime;
using Mosaic.Transport.InMemory;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

// Per-container scoped tracker — each container's handler resolves its own instance, so we can
// assert separately whether the publisher's and subscriber's handlers fired.
public sealed class CrossContainerTracker
{
    private int _count;
    public int InvocationCount => _count;
    public void Increment() => System.Threading.Interlocked.Increment(ref _count);
}

public sealed record CrossContainerEvent(int Value) : IEvent;

public sealed class CrossContainerHandler(CrossContainerTracker tracker) : IEventHandler<CrossContainerEvent>
{
    public ValueTask Handle(CrossContainerEvent notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        tracker.Increment();
        return default;
    }
}

// Per-container singleton that controls whether the handler should throw. Publisher registers it
// with ThrowOn=0 (never fail in-process); subscriber registers it with ThrowOn=int.MaxValue (always
// fail when delivered via transport). Lets us drive the resilience path without interfering with
// the publisher's local dispatch.
public sealed class FailureMode
{
    public int ThrowOnFirstN { get; set; }
    private int _attempts;
    public int Attempts => _attempts;
    public bool ShouldThrowNow() => System.Threading.Interlocked.Increment(ref _attempts) <= ThrowOnFirstN;
}

public sealed record FailingEvent(string Payload) : IEvent;

public sealed class ConditionalFailingHandler(FailureMode mode) : IEventHandler<FailingEvent>
{
    public ValueTask Handle(FailingEvent notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        if (mode.ShouldThrowNow()) throw new InvalidOperationException($"intentional test failure on attempt {mode.Attempts}");
        return default;
    }
}

public class InMemoryTransportTests
{
    [Fact]
    public async Task Publish_in_one_container_reaches_handlers_in_a_peer_container()
    {
        var publisher = new ServiceCollection()
            .AddSingleton<CrossContainerTracker>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic().UseInMemoryTransport("xc-test").Services
            .BuildServiceProvider();

        var subscriber = new ServiceCollection()
            .AddSingleton<CrossContainerTracker>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic().UseInMemoryTransport("xc-test").Services
            .BuildServiceProvider();

        _ = publisher.GetRequiredService<IEventTransport>();
        _ = subscriber.GetRequiredService<IEventTransport>();

        var publisherTracker = publisher.GetRequiredService<CrossContainerTracker>();
        var subscriberTracker = subscriber.GetRequiredService<CrossContainerTracker>();

        using (var scope = publisher.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();
            await engine.Publish(new CrossContainerEvent(42));
        }

        publisherTracker.InvocationCount.ShouldBe(1);
        subscriberTracker.InvocationCount.ShouldBe(1);

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }

    [Fact]
    public async Task Persistently_failing_handler_in_subscriber_lands_in_dead_letter_store()
    {
        var publisher = BuildContainer("dlq-test", failureModeFor: m => m.ThrowOnFirstN = 0);
        var subscriber = BuildContainer("dlq-test", failureModeFor: m => m.ThrowOnFirstN = int.MaxValue,
            tightRetry: true);

        _ = publisher.GetRequiredService<IEventTransport>();
        _ = subscriber.GetRequiredService<IEventTransport>();

        using (var scope = publisher.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompositionEngine>().Publish(new FailingEvent("p"));
        }

        // Subscriber's MaxAttempts=3 → handler fired 3 times before dead-lettering.
        subscriber.GetRequiredService<FailureMode>().Attempts.ShouldBe(3);

        var dlq = (InMemoryDeadLetterStore)subscriber.GetRequiredService<IDeadLetterStore>();
        var letters = dlq.Snapshot();
        letters.Count.ShouldBe(1);
        letters[0].TypeFullName.ShouldBe("Mosaic.Tests.FailingEvent");
        letters[0].ErrorMessage.ShouldContain("intentional test failure");

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }

    [Fact]
    public async Task Transient_failure_recovers_within_retry_budget()
    {
        // Throws on attempts 1+2, succeeds on attempt 3 — within the 3-attempt budget.
        var publisher = BuildContainer("transient-test", failureModeFor: m => m.ThrowOnFirstN = 0);
        var subscriber = BuildContainer("transient-test", failureModeFor: m => m.ThrowOnFirstN = 2,
            tightRetry: true);

        _ = publisher.GetRequiredService<IEventTransport>();
        _ = subscriber.GetRequiredService<IEventTransport>();

        using (var scope = publisher.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompositionEngine>().Publish(new FailingEvent("p"));
        }

        subscriber.GetRequiredService<FailureMode>().Attempts.ShouldBe(3);
        var dlq = (InMemoryDeadLetterStore)subscriber.GetRequiredService<IDeadLetterStore>();
        dlq.Snapshot().ShouldBeEmpty();

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }

    [Fact]
    public async Task Publishes_do_not_loop_back_to_the_publisher()
    {
        var sp = new ServiceCollection()
            .AddSingleton<CrossContainerTracker>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic().UseInMemoryTransport("loopback-test").Services
            .BuildServiceProvider();

        _ = sp.GetRequiredService<IEventTransport>();

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompositionEngine>().Publish(new CrossContainerEvent(99));
        }

        sp.GetRequiredService<CrossContainerTracker>().InvocationCount.ShouldBe(1);
        await sp.DisposeAsync();
    }

    private static ServiceProvider BuildContainer(string channel, Action<FailureMode> failureModeFor, bool tightRetry = false)
    {
        var failureMode = new FailureMode();
        failureModeFor(failureMode);

        var services = new ServiceCollection()
            .AddSingleton(failureMode)
            .AddSingleton<CrossContainerTracker>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        if (tightRetry)
        {
            services.AddSingleton(new MosaicResilienceOptions { MaxAttempts = 3, InitialRetryDelay = TimeSpan.FromMilliseconds(1) });
        }

        services.AddMosaic().UseInMemoryTransport(channel);
        return services.BuildServiceProvider();
    }
}
