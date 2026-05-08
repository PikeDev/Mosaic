namespace Mosaic.Runtime;

/// <summary>
/// Default <see cref="IMessageAuditStore"/> registered by <c>AddMosaic()</c>: discards every
/// entry. Replace via <c>builder.UseAuditing&lt;TStore&gt;()</c> when you want a real audit
/// trail (e.g. <see cref="InMemoryMessageAuditStore"/> for tests, an EF Core / queue adapter
/// for production).
/// </summary>
public sealed class NoOpMessageAuditStore : IMessageAuditStore
{
    public static readonly NoOpMessageAuditStore Instance = new();

    public System.Threading.Tasks.ValueTask WriteAsync(
        MessageAuditEntry entry,
        System.Threading.CancellationToken cancellationToken = default)
        => default;
}
