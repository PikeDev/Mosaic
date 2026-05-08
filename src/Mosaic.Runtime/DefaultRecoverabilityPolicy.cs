namespace Mosaic.Runtime;

/// <summary>
/// Default <see cref="IRecoverabilityPolicy"/> registered by <c>AddMosaic()</c>: retry with the
/// <see cref="MosaicResilienceOptions"/> backoff curve up to
/// <see cref="MosaicResilienceOptions.MaxAttempts"/>, then dead-letter.
/// <para>
/// Cancellation tokens are observed by the dispatch loop, not by the policy — operation-cancelled
/// exceptions short-circuit before the policy is consulted, so this never sees them.
/// </para>
/// </summary>
public sealed class DefaultRecoverabilityPolicy : IRecoverabilityPolicy
{
    private readonly MosaicResilienceOptions _options;

    public DefaultRecoverabilityPolicy(MosaicResilienceOptions options)
    {
        _options = options;
    }

    public RecoverabilityAction Decide(RecoverabilityContext context)
    {
        if (context.AttemptNumber >= _options.MaxAttempts) return RecoverabilityAction.DeadLetter;
        return RecoverabilityAction.Retry(_options.RetryDelayFor(context.AttemptNumber));
    }
}
