using Microsoft.Extensions.DependencyInjection;
using Mosaic;
using Mosaic.Runtime;
using Mosaic.Sample.AuditTrail;

var services = new ServiceCollection();
services.AddMosaic().UseInMemoryAuditing();

await using var sp = services.BuildServiceProvider();

// Drive the chain: OrderPlaced → handler publishes OrderAccepted → handler publishes ShipmentArranged.
// Three events, two cascaded publishes — all happen in this single process, all share one
// correlation id, each one stitches via causation to the message that triggered it.
using (var scope = sp.CreateScope())
{
    var engine = scope.ServiceProvider.GetRequiredService<ICompositionEngine>();
    Console.WriteLine("══════ Driving the chain: OrderPlaced ══════");
    await engine.Publish(new OrderPlaced(OrderId: 4711));
}

Console.WriteLine();
Console.WriteLine("══════ Audit trail (grouped by CorrelationId) ══════");

var audit = sp.GetRequiredService<InMemoryMessageAuditStore>();
var snapshot = audit.Snapshot();

foreach (var group in snapshot.GroupBy(e => e.Headers.CorrelationId))
{
    Console.WriteLine();
    Console.WriteLine($"correlation: {group.Key}");

    // Walk the chain root-first by following CausationId. With buffered events the audit log is
    // written leaf-first (the deepest cascaded publish completes before the parent terminal returns),
    // so an operator reading the trail wants the parent-pointer view rather than the wall-clock view.
    var entries = group.ToList();
    var byMessageId = entries.ToDictionary(e => e.Headers.MessageId.ToString("N"));
    var children = entries.Where(e => e.Headers.CausationId is not null)
                          .GroupBy(e => e.Headers.CausationId!)
                          .ToDictionary(g => g.Key, g => g.ToList());

    void Walk(MessageAuditEntry entry, int depth)
    {
        var indent = new string(' ', depth * 2);
        var causation = entry.Headers.CausationId is null ? "(root)" : entry.Headers.CausationId;
        Console.WriteLine(
            $"  {indent}{entry.Direction,-8}  {entry.MessageType,-50}  msg={entry.Headers.MessageId:N}  cause={causation}");
        if (children.TryGetValue(entry.Headers.MessageId.ToString("N"), out var kids))
            foreach (var child in kids) Walk(child, depth + 1);
    }

    foreach (var root in entries.Where(e => e.Headers.CausationId is null))
    {
        Walk(root, 0);
    }
}
