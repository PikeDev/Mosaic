namespace Mosaic.Outbox.EFCore;

/// <summary>
/// Outbox row mapped via the consumer's <see cref="IMosaicOutboxDbContext"/>. Lives in whatever
/// schema/table the consumer's DbContext is configured for; the relay reads pending rows + ships
/// them via the underlying transport.
/// </summary>
public sealed class MosaicOutboxEntry
{
    public System.Guid Id { get; set; }
    public string Sender { get; set; } = "";
    public string TypeFullName { get; set; } = "";
    public byte[] Payload { get; set; } = System.Array.Empty<byte>();
    public System.DateTime QueuedAt { get; set; }
    public System.DateTime? SentAt { get; set; }

    /// <summary>Correlation graph: chain root id (shared across every message in the same flow).</summary>
    public string CorrelationId { get; set; } = "";

    /// <summary>Correlation graph: id of the message that caused this one — null at chain root.</summary>
    public string? CausationId { get; set; }
}
