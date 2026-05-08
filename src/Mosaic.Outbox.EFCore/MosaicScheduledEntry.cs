namespace Mosaic.Outbox.EFCore;

/// <summary>
/// Row backing <see cref="EFCoreScheduledMessageStore{TDbContext}"/>: one entry per pending
/// scheduled message. <see cref="EFCoreScheduledRelayHostedService{TDbContext}"/> polls for rows
/// where <see cref="DispatchedAt"/> is null and <see cref="DueAt"/> is in the past, dispatches
/// them via <see cref="IInboundEventDispatcher"/>, then stamps <see cref="DispatchedAt"/>.
/// </summary>
public sealed class MosaicScheduledEntry
{
    public System.Guid Id { get; set; }
    public string DedupKey { get; set; } = "";
    public System.DateTime DueAt { get; set; }
    public string TypeFullName { get; set; } = "";
    public byte[] Payload { get; set; } = System.Array.Empty<byte>();
    public System.DateTime QueuedAt { get; set; }
    public System.DateTime? DispatchedAt { get; set; }

    /// <summary>Correlation graph: chain root id (shared across every message in the same flow).</summary>
    public string CorrelationId { get; set; } = "";

    /// <summary>Correlation graph: id of the message that caused this one — null at chain root.</summary>
    public string? CausationId { get; set; }
}
