namespace Mosaic.Runtime;

/// <summary>
/// Stable per-process identifier embedded in outbound transport envelopes so receivers can drop
/// their own messages back from the bus (loopback suppression). Registered as a singleton —
/// every Mosaic component in the same process uses the same id.
/// </summary>
public sealed class ProcessSenderId
{
    public string Value { get; } = System.Guid.NewGuid().ToString("N");
}
