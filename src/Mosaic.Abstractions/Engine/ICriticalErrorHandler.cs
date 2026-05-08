namespace Mosaic;

/// <summary>
/// What the critical-error handler sees. Carries the message text the framework chose, the
/// underlying exception (if any), and the message metadata when the failure is tied to a
/// specific in-flight message hop. <see cref="MessageType"/> and <see cref="Headers"/> are
/// null for endpoint-wide failures (e.g. background relay died, DLQ store unreachable).
/// </summary>
public sealed record CriticalErrorContext(
    string Message,
    System.Exception? Exception = null,
    string? MessageType = null,
    MessageHeaders? Headers = null);

/// <summary>
/// Framework hook for catastrophic conditions the runtime can detect but cannot recover from on
/// its own — examples: the dead-letter store itself threw, the recoverability policy returned an
/// unknown verdict, a background relay died. The default handler logs at <c>Critical</c> level;
/// real deployments register a custom handler to escalate (page on-call, stop the endpoint, send
/// Slack/PagerDuty), per ADSD §14.2 ("the error queue is the primary signal — act on the spike").
/// <para>
/// Implementations should not throw — the runtime swallows handler exceptions to avoid escalating
/// noise into a second critical error. Side effects (network calls, process termination) are the
/// custom handler's responsibility, not the framework's.
/// </para>
/// </summary>
public interface ICriticalErrorHandler
{
    System.Threading.Tasks.ValueTask HandleAsync(
        CriticalErrorContext context,
        System.Threading.CancellationToken cancellationToken = default);
}
