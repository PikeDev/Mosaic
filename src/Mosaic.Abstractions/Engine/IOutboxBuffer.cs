using System.Buffers;

namespace Mosaic;

/// <summary>
/// Per-scope sink that <see cref="ICompositionContext.Publish{TEvent}"/> drops outbound events
/// into when an atomic-outbox adapter is registered. The adapter (e.g. <c>Mosaic.Outbox.EFCore</c>)
/// pushes the row into the consumer's persistence change-set so it commits in the same transaction
/// as state changes — atomic outbox.
/// <para>
/// When no buffer is registered (default), this seam is unused; events flow through the normal
/// in-process + transport path.
/// </para>
/// </summary>
public interface IOutboxBuffer
{
    /// <summary>Synchronously stage an already-serialised event payload into the consumer's
    /// change-set. Implementations that need to retain the bytes past this call (most do — the
    /// row is committed at next SaveChanges) should copy out of <paramref name="payload"/>.
    /// <paramref name="headers"/> are stored on the outbox row so the relay can ship them on the
    /// wire and receivers see the same correlation graph the publisher saw.</summary>
    void Enqueue(string typeFullName, MessageHeaders headers, ReadOnlySequence<byte> payload);
}
