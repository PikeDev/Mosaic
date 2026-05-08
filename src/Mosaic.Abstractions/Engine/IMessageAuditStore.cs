namespace Mosaic;

/// <summary>
/// Direction of an audited message hop relative to the local process.
/// <see cref="Sent"/> entries are written after the engine hands a message to its transport;
/// <see cref="Received"/> entries are written after a transport-delivered message is successfully
/// dispatched to its handlers.
/// </summary>
public enum MessageAuditDirection
{
    Sent,
    Received,
}

/// <summary>
/// One row of the message audit log — a single hop captured for the correlation graph.
/// Combine the <see cref="Sent"/> from publisher A and the matching <see cref="Received"/> on
/// subscriber B (same <see cref="MessageHeaders.MessageId"/>) to reconstruct one wire delivery;
/// chain on <see cref="MessageHeaders.CorrelationId"/> to walk the entire async flow.
/// </summary>
public sealed record MessageAuditEntry(
    System.DateTime TimestampUtc,
    string MessageType,
    MessageAuditDirection Direction,
    MessageHeaders Headers);

/// <summary>
/// Pluggable durable sink for <see cref="MessageAuditEntry"/> rows. Opt-in: the default
/// registration is a no-op store, replaced by <c>UseAuditing&lt;TStore&gt;()</c> on the builder.
/// <para>
/// Implementations should be cheap to call on the hot path (publish + inbound dispatch) — buffer
/// or batch internally if persistence is expensive. The framework calls <see cref="WriteAsync"/>
/// after the message hop has succeeded; failures are logged but do not roll back the hop.
/// </para>
/// </summary>
public interface IMessageAuditStore
{
    System.Threading.Tasks.ValueTask WriteAsync(
        MessageAuditEntry entry,
        System.Threading.CancellationToken cancellationToken = default);
}
