namespace Mosaic.Outbox.Postgres;

/// <summary>One row of the <c>mosaic_outbox</c> table — a pending or already-shipped event.</summary>
public sealed record OutboxEntry(
    System.Guid Id,
    string Sender,
    string TypeFullName,
    byte[] Payload,
    System.DateTime QueuedAt,
    System.DateTime? SentAt,
    string CorrelationId,
    string? CausationId);
