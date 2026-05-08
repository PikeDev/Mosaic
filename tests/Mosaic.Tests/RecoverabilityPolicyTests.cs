using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mosaic.Runtime;
using Mosaic.Transport.InMemory;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

// Two distinct error types so the policy can branch on them. Models the canonical webshop
// scenario: gateway-class transients deserve retry; validation/contract failures don't.
public sealed class GatewayTimeoutException(string message) : Exception(message);
public sealed class PoisonPayloadException(string message) : Exception(message);

// Singleton state — the handler itself is Scoped (source-gen default), so per-attempt
// invocations resolve fresh instances. State that must persist across attempts goes here.
public sealed class FlakyState
{
    private int _timeoutFailuresLeft;
    private int _invocations;
    public bool ThrowPoison { get; set; }
    public int InvocationCount => _invocations;
    public void SetTimeoutFailures(int n) => System.Threading.Interlocked.Exchange(ref _timeoutFailuresLeft, n);
    public int RegisterInvocation() => System.Threading.Interlocked.Increment(ref _invocations);
    public int ConsumeTimeoutBudget() => System.Threading.Interlocked.Decrement(ref _timeoutFailuresLeft);
}

public sealed record FlakyEvent(string Marker) : IEvent;

public sealed class FlakyHandler(FlakyState state) : IEventHandler<FlakyEvent>
{
    public ValueTask Handle(FlakyEvent notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        state.RegisterInvocation();
        if (state.ThrowPoison) throw new PoisonPayloadException("contract violation: " + notification.Marker);
        // Decrement before deciding whether to throw — remaining ≥ 0 means we just consumed a
        // budget slot, so this attempt should fail. remaining < 0 means the budget is spent and
        // the next call (this one) should succeed.
        var remaining = state.ConsumeTimeoutBudget();
        if (remaining >= 0)
        {
            throw new GatewayTimeoutException($"gateway transient (remaining flakiness: {remaining})");
        }
        return default;
    }
}

public class RecoverabilityPolicyTests
{
    [Fact]
    public void Default_policy_is_DefaultRecoverabilityPolicy_until_replaced()
    {
        var sp = new ServiceCollection().AddMosaic().Services.BuildServiceProvider();
        sp.GetRequiredService<IRecoverabilityPolicy>().ShouldBeOfType<DefaultRecoverabilityPolicy>();
    }

    [Fact]
    public void UseRecoverability_with_delegate_replaces_default()
    {
        var sp = new ServiceCollection()
            .AddMosaic()
            .UseRecoverability(_ => RecoverabilityAction.DeadLetter)
            .Services
            .BuildServiceProvider();

        sp.GetRequiredService<IRecoverabilityPolicy>().ShouldNotBeOfType<DefaultRecoverabilityPolicy>();
    }

    [Fact]
    public async Task Custom_policy_DLQs_poison_exceptions_immediately_with_no_retries()
    {
        // Publisher: benign state (its in-process handler shouldn't throw, otherwise the local
        // dispatch errors before the transport hop ever happens).
        // Subscriber: ThrowPoison so its transport-delivered handler throws.
        // Policy: PoisonPayloadException → DLQ on first failure (retries don't help — same input).
        var publisher = BuildContainer("recover-poison", _ => { },
            _ => RecoverabilityAction.DeadLetter);

        var subscriber = BuildContainer("recover-poison", s => s.ThrowPoison = true,
            ctx => ctx.Exception is PoisonPayloadException
                ? RecoverabilityAction.DeadLetter
                : RecoverabilityAction.DeadLetter);

        _ = publisher.GetRequiredService<IEventTransport>();
        _ = subscriber.GetRequiredService<IEventTransport>();

        using (var scope = publisher.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompositionEngine>()
                .Publish(new FlakyEvent("poison-1"));
        }

        // Subscriber's handler fired exactly once — no retries.
        subscriber.GetRequiredService<FlakyState>().InvocationCount.ShouldBe(1);

        // The dead-letter store on the subscriber side captured the failure.
        var dlq = (InMemoryDeadLetterStore)subscriber.GetRequiredService<IDeadLetterStore>();
        var letters = dlq.Snapshot();
        letters.Count.ShouldBe(1);
        letters[0].ErrorMessage.ShouldContain("contract violation");

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }

    [Fact]
    public async Task Custom_policy_retries_gateway_transients_aggressively_and_recovers_within_budget()
    {
        // Subscriber throws on first 2 attempts, succeeds on 3rd. Custom policy retries
        // GatewayTimeoutException with a tiny delay, no ceiling — exactly the fix that an
        // overly-aggressive default DLQ would have prevented.
        // Publisher: benign — only the subscriber should be flaky. Otherwise the publisher's own
        // in-process dispatch retries too, polluting the assertion.
        var publisher = BuildContainer("recover-gateway", _ => { },
            _ => RecoverabilityAction.DeadLetter);

        var subscriber = BuildContainer("recover-gateway", s => s.SetTimeoutFailures(2),
            ctx => ctx.Exception is GatewayTimeoutException
                ? RecoverabilityAction.Retry(TimeSpan.FromMilliseconds(1))
                : RecoverabilityAction.DeadLetter);

        _ = publisher.GetRequiredService<IEventTransport>();
        _ = subscriber.GetRequiredService<IEventTransport>();

        using (var scope = publisher.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompositionEngine>()
                .Publish(new FlakyEvent("gateway-1"));
        }

        // Three attempts total: 2 throws, 1 success.
        subscriber.GetRequiredService<FlakyState>().InvocationCount.ShouldBe(3);

        // Nothing dead-lettered — the policy kept retrying until success.
        var dlq = (InMemoryDeadLetterStore)subscriber.GetRequiredService<IDeadLetterStore>();
        dlq.Snapshot().ShouldBeEmpty();

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }

    private static ServiceProvider BuildContainer(string channel, Action<FlakyState> configureState, Func<RecoverabilityContext, RecoverabilityAction> policy)
    {
        var state = new FlakyState();
        configureState(state);

        return new ServiceCollection()
            .AddSingleton(state)
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic()
                .UseRecoverability(policy)
                .UseInMemoryTransport(channel).Services
            .BuildServiceProvider();
    }
}
