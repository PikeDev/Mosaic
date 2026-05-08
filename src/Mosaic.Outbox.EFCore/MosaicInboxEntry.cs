namespace Mosaic.Outbox.EFCore;

/// <summary>
/// Row backing <see cref="EFCoreInboxStore{TDbContext}"/>: one entry per consumed inbound message.
/// The presence of a row keyed on <see cref="MessageId"/> means "this process has already
/// processed that envelope" — a redelivery can be skipped.
/// </summary>
public sealed class MosaicInboxEntry
{
    public System.Guid MessageId { get; set; }
    public System.DateTime ConsumedAt { get; set; }
}
