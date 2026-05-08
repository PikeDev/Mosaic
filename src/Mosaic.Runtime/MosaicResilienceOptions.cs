namespace Mosaic.Runtime;

/// <summary>
/// Knobs for inbound-dispatch resilience: how many retries to attempt + the backoff between them.
/// Defaults: 3 attempts, exponential backoff starting at 100ms (100, 200, 400, …). Tune via
/// transport configuration callbacks.
/// </summary>
public sealed class MosaicResilienceOptions
{
    /// <summary>Total attempts including the first. 1 disables retry.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base delay between attempts; doubled per attempt for exponential backoff.</summary>
    public System.TimeSpan InitialRetryDelay { get; set; } = System.TimeSpan.FromMilliseconds(100);

    /// <summary>Delay to wait before retry attempt <paramref name="attempt"/> (1-based).</summary>
    public System.TimeSpan RetryDelayFor(int attempt)
    {
        // attempt 1 failed → wait InitialRetryDelay; attempt 2 → 2× ; attempt 3 → 4× ; …
        var multiplier = 1 << System.Math.Max(0, attempt - 1);
        return System.TimeSpan.FromTicks(InitialRetryDelay.Ticks * multiplier);
    }
}
