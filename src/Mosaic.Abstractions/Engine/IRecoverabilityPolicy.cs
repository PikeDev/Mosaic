namespace Mosaic;

/// <summary>
/// What the framework should do with a message whose handler just threw. The policy returns one
/// of these per failed attempt — the recovery loop in <c>InboundDispatch</c> obeys the verdict.
/// </summary>
public enum RecoverabilityKind
{
    /// <summary>Wait <see cref="RecoverabilityAction.Delay"/>, then dispatch again.</summary>
    DelayedRetry,

    /// <summary>Persist to the dead-letter store and stop trying. The default terminal action.</summary>
    DeadLetter,

    /// <summary>Drop the message silently — no retry, no dead-letter row, no audit failure.
    /// Use sparingly: for messages whose loss is acceptable (debug pings, expired notifications).</summary>
    Discard,
}

/// <summary>
/// Decision returned by an <see cref="IRecoverabilityPolicy"/>. Use the static factories
/// rather than constructing directly — they document the intent at the call site.
/// </summary>
public readonly record struct RecoverabilityAction(RecoverabilityKind Kind, System.TimeSpan Delay = default)
{
    /// <summary>Retry after <paramref name="delay"/>. Pass <see cref="System.TimeSpan.Zero"/> for an immediate retry.</summary>
    public static RecoverabilityAction Retry(System.TimeSpan delay) => new(RecoverabilityKind.DelayedRetry, delay);

    /// <summary>Move the failed message to the dead-letter store. Terminal — no further attempts.</summary>
    public static readonly RecoverabilityAction DeadLetter = new(RecoverabilityKind.DeadLetter);

    /// <summary>Drop the message silently. Terminal.</summary>
    public static readonly RecoverabilityAction Discard = new(RecoverabilityKind.Discard);
}

/// <summary>
/// What the policy sees when deciding. Carries the message's type, its correlation graph, the
/// 1-based attempt number, and the exception itself. Policies that care about a configured
/// ceiling inject <see cref="Mosaic.Runtime.MosaicResilienceOptions"/> themselves — keeping it
/// off the context lets the policy be the sole owner of "how many attempts is too many".
/// </summary>
public sealed record RecoverabilityContext(
    string MessageType,
    MessageHeaders Headers,
    int AttemptNumber,
    System.Exception Exception);

/// <summary>
/// Pluggable per-failure policy. Replaces Mosaic's hard-coded
/// <c>"retry MaxAttempts times then DLQ"</c> with a function the consumer owns: route specific
/// exception types to immediate DLQ (poison message — no retry helps), retry network-class
/// failures aggressively, discard expired notifications, and so on.
/// <para>
/// Implementations should be cheap and side-effect free — they're called on every failed attempt.
/// Side effects (notifications, ops paging) belong in a <see cref="IDeadLetterStore"/> decorator
/// or an outbound audit pipeline, not here.
/// </para>
/// <para>
/// ADSD §2.6 / §14.2: the error queue is the primary signal. A custom policy lets each service
/// classify what "noise" vs "real failure" means for its own bounded context.
/// </para>
/// </summary>
public interface IRecoverabilityPolicy
{
    RecoverabilityAction Decide(RecoverabilityContext context);
}
