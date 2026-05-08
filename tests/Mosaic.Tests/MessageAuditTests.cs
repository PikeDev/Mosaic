using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mosaic.Runtime;
using Mosaic.Transport.InMemory;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

// Three-event chain so we can prove the correlation graph stitches across cascaded publishes:
//   ChainStart → handler raises ChainMiddle → handler raises ChainEnd
public sealed record ChainStart(string Tag) : IEvent;
public sealed record ChainMiddle(string Tag) : IEvent;
public sealed record ChainEnd(string Tag) : IEvent;

public sealed class ChainStartHandler : IEventHandler<ChainStart>
{
    public async ValueTask Handle(ChainStart notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        await context.Publish(new ChainMiddle(notification.Tag), cancellationToken);
    }
}

public sealed class ChainMiddleHandler : IEventHandler<ChainMiddle>
{
    public async ValueTask Handle(ChainMiddle notification, ICompositionContext context, CancellationToken cancellationToken)
    {
        await context.Publish(new ChainEnd(notification.Tag), cancellationToken);
    }
}

public sealed class ChainEndHandler : IEventHandler<ChainEnd>
{
    public ValueTask Handle(ChainEnd notification, ICompositionContext context, CancellationToken cancellationToken) => default;
}

public class MessageAuditTests
{
    [Fact]
    public async Task Default_audit_store_is_NoOp_when_UseAuditing_is_not_called()
    {
        var sp = new ServiceCollection().AddMosaic().Services.BuildServiceProvider();
        sp.GetRequiredService<IMessageAuditStore>().ShouldBeOfType<NoOpMessageAuditStore>();
    }

    [Fact]
    public async Task UseInMemoryAuditing_replaces_default_with_queryable_store()
    {
        var sp = new ServiceCollection().AddMosaic().UseInMemoryAuditing().Services.BuildServiceProvider();
        sp.GetRequiredService<IMessageAuditStore>().ShouldBeOfType<InMemoryMessageAuditStore>();
        // Same instance is also resolvable by concrete type (no downcast needed for assertions).
        sp.GetRequiredService<InMemoryMessageAuditStore>()
          .ShouldBeSameAs(sp.GetRequiredService<IMessageAuditStore>());
    }

    [Fact]
    public async Task In_process_chain_records_one_correlation_for_every_hop_and_links_via_causation()
    {
        // A single in-process container — no transport involved. Each Publish hop still walks
        // through engine.Publish, so the audit pipeline records a 'Sent' entry per hop. The chain
        // (ChainStart → ChainMiddle → ChainEnd) shares one CorrelationId; each link's CausationId
        // points back at the previous link's MessageId.
        await using var sp = new ServiceCollection()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic().UseInMemoryAuditing().Services
            .BuildServiceProvider();

        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();

        await engine.Publish(new ChainStart("alpha"));

        var audit = sp.GetRequiredService<InMemoryMessageAuditStore>();
        var entries = audit.Snapshot();
        entries.Count.ShouldBe(3);   // one Sent per hop in this 3-link chain

        // Walk: every entry shares the chain-root CorrelationId.
        var corr = entries[0].Headers.CorrelationId;
        entries.ShouldAllBe(e => e.Headers.CorrelationId == corr);

        // The chain is stitched: ChainMiddle's CausationId is ChainStart's MessageId, and
        // ChainEnd's CausationId is ChainMiddle's MessageId. (In-process publish runs synchronously
        // inside the parent handler, so order in the audit log mirrors emission order.)
        var byType = entries.ToDictionary(e => e.MessageType);
        byType[typeof(ChainStart).FullName!].Headers.CausationId.ShouldBeNull();
        byType[typeof(ChainMiddle).FullName!].Headers.CausationId
            .ShouldBe(byType[typeof(ChainStart).FullName!].Headers.MessageId.ToString("N"));
        byType[typeof(ChainEnd).FullName!].Headers.CausationId
            .ShouldBe(byType[typeof(ChainMiddle).FullName!].Headers.MessageId.ToString("N"));
    }

    [Fact]
    public async Task Cross_container_publish_records_Sent_in_publisher_and_Received_in_subscriber_with_same_message_id()
    {
        // Two containers connected via the in-memory transport. The publisher logs a 'Sent' row;
        // the subscriber logs a 'Received' row for the same MessageId — proves the correlation
        // graph survives the transport hop.
        var publisher = BuildContainer("audit-xc-test");
        var subscriber = BuildContainer("audit-xc-test");

        _ = publisher.GetRequiredService<IEventTransport>();
        _ = subscriber.GetRequiredService<IEventTransport>();

        using (var scope = publisher.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICompositionEngine>()
                .Publish(new ChainEnd("xc"));
        }

        var publisherAudit = publisher.GetRequiredService<InMemoryMessageAuditStore>();
        var subscriberAudit = subscriber.GetRequiredService<InMemoryMessageAuditStore>();

        var sent = publisherAudit.Snapshot().Single();
        sent.Direction.ShouldBe(MessageAuditDirection.Sent);
        sent.MessageType.ShouldBe(typeof(ChainEnd).FullName);

        // The subscriber sees the same MessageId on its Received entry — the wire envelope
        // round-tripped the headers verbatim.
        var received = subscriberAudit.Snapshot().Single();
        received.Direction.ShouldBe(MessageAuditDirection.Received);
        received.Headers.MessageId.ShouldBe(sent.Headers.MessageId);
        received.Headers.CorrelationId.ShouldBe(sent.Headers.CorrelationId);

        await publisher.DisposeAsync();
        await subscriber.DisposeAsync();
    }

    private static ServiceProvider BuildContainer(string channel)
        => new ServiceCollection()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMosaic().UseInMemoryAuditing().UseInMemoryTransport(channel).Services
            .BuildServiceProvider();
}
