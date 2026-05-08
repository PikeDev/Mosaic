using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mosaic.Runtime;
using Mosaic.Transport.InMemory;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

// Per-container switch so the publisher's in-process dispatch stays benign while the
// subscriber's transport-delivered dispatch always throws → policy → DLQ → critical error.
public sealed class CritErrorFailureGate
{
    public bool Enabled { get; set; }
}

public sealed record AlwaysFailingEvent(string Tag) : IEvent;

public sealed class AlwaysFailingHandler(CritErrorFailureGate gate) : IEventHandler<AlwaysFailingEvent>
{
    public ValueTask Handle(AlwaysFailingEvent notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        if (gate.Enabled) throw new InvalidOperationException("intentional handler failure: " + notification.Tag);
        return default;
    }
}

// Stand-in for a broken dead-letter store — disk full, queue unreachable, etc.
public sealed class ThrowingDeadLetterStore : IDeadLetterStore
{
    public Task WriteAsync(string typeFullName, ReadOnlySequence<byte> payload, string errorMessage, string errorStack, CancellationToken cancellationToken)
        => throw new InvalidOperationException("simulated DLQ outage: storage backend unreachable");
}

public sealed class CapturingCriticalErrorHandler : ICriticalErrorHandler
{
    private readonly ConcurrentQueue<CriticalErrorContext> _captured = new();
    public IReadOnlyList<CriticalErrorContext> Captured => _captured.ToArray();
    public ValueTask HandleAsync(CriticalErrorContext context, CancellationToken cancellationToken)
    {
        _captured.Enqueue(context);
        return default;
    }
}

public sealed class ThrowingCriticalErrorHandler : ICriticalErrorHandler
{
    public ValueTask HandleAsync(CriticalErrorContext context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("intentional critical-handler failure");
}

public class CriticalErrorHandlerTests
{
    [Fact]
    public void Default_handler_is_LoggingCriticalErrorHandler_until_replaced()
    {
        var sp = new ServiceCollection()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic().Services
            .BuildServiceProvider();

        sp.GetRequiredService<ICriticalErrorHandler>().ShouldBeOfType<LoggingCriticalErrorHandler>();
    }

    [Fact]
    public async Task DLQ_store_failure_escalates_through_critical_error_handler()
    {
        var critical = new CapturingCriticalErrorHandler();

        // Subscriber: handler throws → policy → DLQ → DLQ throws → critical-error fires.
        // Publisher: gate disabled so its local in-process dispatch passes through cleanly.
        var subscriber = new ServiceCollection()
            .AddSingleton(new CritErrorFailureGate { Enabled = true })
            .AddSingleton<IDeadLetterStore, ThrowingDeadLetterStore>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic()
                .UseCriticalErrorHandler(critical)
                .UseInMemoryTransport("crit-dlq-fail").Services
            .BuildServiceProvider();

        var publisher = new ServiceCollection()
            .AddSingleton(new CritErrorFailureGate { Enabled = false })
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic().UseInMemoryTransport("crit-dlq-fail").Services
            .BuildServiceProvider();

        _ = subscriber.GetRequiredService<IEventTransport>();
        _ = publisher.GetRequiredService<IEventTransport>();

        using (var scope = publisher.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompositionEngine>()
                .Publish(new AlwaysFailingEvent("ka-boom"));
        }

        critical.Captured.Count.ShouldBe(1);
        var ctx = critical.Captured[0];
        ctx.Message.ShouldContain("Dead-letter store unavailable");
        ctx.MessageType.ShouldBe(typeof(AlwaysFailingEvent).FullName);
        ctx.Headers.ShouldNotBeNull();
        ctx.Exception.ShouldBeOfType<AggregateException>();
        // The aggregate carries both the DLQ failure AND the original handler failure for triage.
        ((AggregateException)ctx.Exception!).InnerExceptions
            .ShouldContain(e => e.Message.Contains("simulated DLQ outage"));
        ((AggregateException)ctx.Exception!).InnerExceptions
            .ShouldContain(e => e.Message.Contains("intentional handler failure"));

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }

    [Fact]
    public async Task Critical_handler_that_throws_is_swallowed_and_does_not_break_dispatch()
    {
        // Both DLQ AND critical-handler are broken — the dispatch loop must not propagate the
        // critical-handler exception. (It would otherwise cascade into Task.Run unobserved-task
        // territory and silently degrade the host.)
        var subscriber = new ServiceCollection()
            .AddSingleton(new CritErrorFailureGate { Enabled = true })
            .AddSingleton<IDeadLetterStore, ThrowingDeadLetterStore>()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic()
                .UseCriticalErrorHandler<ThrowingCriticalErrorHandler>()
                .UseInMemoryTransport("crit-double-fail").Services
            .BuildServiceProvider();

        var publisher = new ServiceCollection()
            .AddSingleton(new CritErrorFailureGate { Enabled = false })
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic().UseInMemoryTransport("crit-double-fail").Services
            .BuildServiceProvider();

        _ = subscriber.GetRequiredService<IEventTransport>();
        _ = publisher.GetRequiredService<IEventTransport>();

        // Should NOT throw — the framework swallows the critical-handler exception and logs.
        using (var scope = publisher.CreateScope())
        {
            await Should.NotThrowAsync(async () =>
            {
                await scope.ServiceProvider.GetRequiredService<ICompositionEngine>()
                    .Publish(new AlwaysFailingEvent("double-trouble"));
            });
        }

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }
}
