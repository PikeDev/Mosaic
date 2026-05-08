namespace Mosaic;

/// <summary>
/// Wire-level identifiers stamped on every message that crosses an engine boundary
/// (Send → handler, Publish → transport, scheduled → relay). Three IDs together form a directed
/// graph of cause and effect — the same correlation graph Udi Dahan calls "the thing that makes
/// async systems debuggable" (ADSD §4.7):
/// <list type="bullet">
///   <item><see cref="MessageId"/> — unique per message instance. Inbox dedup key.</item>
///   <item><see cref="CorrelationId"/> — shared by every message in the same logical flow
///         (a saga lifecycle, a user request and its cascade). Stays stable across hops.</item>
///   <item><see cref="CausationId"/> — the <see cref="MessageId"/> of the message whose handler
///         produced this one. Null only at the very root of a chain.</item>
/// </list>
/// </summary>
public readonly record struct MessageHeaders(
    System.Guid MessageId,
    string CorrelationId,
    string? CausationId,
    System.DateTime SentAtUtc)
{
    /// <summary>
    /// Headers for a brand-new chain root: fresh <see cref="MessageId"/>, fresh
    /// <see cref="CorrelationId"/>, no causation. Used when an engine API is called from outside
    /// any inbound message context (HTTP request, hosted service tick).
    /// </summary>
    public static MessageHeaders NewRoot()
        => new(System.Guid.NewGuid(),
               System.Guid.NewGuid().ToString("N"),
               null,
               System.DateTime.UtcNow);

    /// <summary>
    /// Headers for a child of <c>this</c> message: fresh <see cref="MessageId"/>, same
    /// <see cref="CorrelationId"/>, <see cref="CausationId"/> set to the parent's
    /// <see cref="MessageId"/>. Used when a handler publishes/schedules a follow-up message —
    /// keeps the chain stitched together.
    /// </summary>
    public MessageHeaders ForChild()
        => new(System.Guid.NewGuid(),
               CorrelationId,
               MessageId.ToString("N"),
               System.DateTime.UtcNow);
}
