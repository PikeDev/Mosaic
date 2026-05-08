using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mosaic.Runtime;

/// <summary>
/// Shared resilience helper that transport implementations call to deliver an inbound event:
/// resolves a fresh DI scope, dispatches, and consults <see cref="IRecoverabilityPolicy"/> on
/// failure to decide whether to retry, dead-letter, or discard. Lives in <see cref="Mosaic.Runtime"/>
/// so every transport implementation gets the same policy-driven recovery semantics without
/// re-implementing them.
/// </summary>
public static class InboundDispatch
{
    public static async System.Threading.Tasks.Task TryDispatchAsync(
        string typeFullName,
        MessageHeaders headers,
        System.Buffers.ReadOnlySequence<byte> payload,
        IServiceScopeFactory scopeFactory,
        IDeadLetterStore deadLetterStore,
        IRecoverabilityPolicy recoverabilityPolicy,
        ICriticalErrorHandler criticalErrorHandler,
        ILogger logger,
        System.Threading.CancellationToken cancellationToken)
    {
        // Loop runs as long as the policy says Retry. The policy owns the ceiling — a buggy
        // policy that always says Retry will spin forever, which is the policy author's problem.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();

                // Inbox dedup: when a store is wired, dedupe by the publisher-stamped MessageId.
                // The MarkConsumed below stages a row that commits atomically with handler state
                // via the consumer's DbContext SaveChanges.
                var inbox = (IInboxStore?)scope.ServiceProvider.GetService(typeof(IInboxStore));
                if (inbox is not null)
                {
                    if (await inbox.WasConsumedAsync(headers.MessageId, cancellationToken).ConfigureAwait(false))
                    {
                        logger.LogDebug("Inbound dispatch skipped (already consumed) for {Type} message {MessageId}.", typeFullName, headers.MessageId);
                        return;
                    }
                }

                var dispatcher = scope.ServiceProvider.GetRequiredService<IInboundEventDispatcher>();
                await dispatcher.DispatchInboundAsync(typeFullName, headers, payload, cancellationToken).ConfigureAwait(false);

                if (inbox is not null)
                {
                    // Stages the inbox row in the consumer's DbContext. Commits via the handler's
                    // SaveChanges (atomic) or via EFCoreOutboxBuffer's dispose-time fallback save.
                    await inbox.MarkConsumedAsync(headers.MessageId, cancellationToken).ConfigureAwait(false);
                }

                // Audit AFTER successful dispatch — receives that retried-then-failed end up in DLQ
                // (see catch below), receives that retried-then-succeeded show up here exactly once.
                // Default store is no-op, so this is a free virtual call when auditing isn't opted in.
                var auditStore = (IMessageAuditStore?)scope.ServiceProvider.GetService(typeof(IMessageAuditStore));
                if (auditStore is not null)
                {
                    await auditStore.WriteAsync(
                        new MessageAuditEntry(System.DateTime.UtcNow, typeFullName, MessageAuditDirection.Received, headers),
                        cancellationToken).ConfigureAwait(false);
                }
                return;
            }
            catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (System.Exception ex)
            {
                var decision = recoverabilityPolicy.Decide(new RecoverabilityContext(
                    typeFullName, headers, attempt, ex));

                switch (decision.Kind)
                {
                    case RecoverabilityKind.DelayedRetry:
                        logger.LogWarning(ex, "Inbound dispatch attempt {Attempt} failed for {Type}; retrying after {Delay}.",
                            attempt, typeFullName, decision.Delay);
                        try { await System.Threading.Tasks.Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false); }
                        catch (System.OperationCanceledException) { return; }
                        continue;

                    case RecoverabilityKind.DeadLetter:
                        logger.LogError(ex, "Inbound dispatch failed for {Type} after {Attempts} attempt(s); dead-lettering.",
                            typeFullName, attempt);
                        await SafeWriteToDeadLetterAsync(deadLetterStore, criticalErrorHandler, typeFullName, headers, payload, ex, logger, cancellationToken)
                            .ConfigureAwait(false);
                        return;

                    case RecoverabilityKind.Discard:
                        logger.LogWarning(ex, "Inbound dispatch failed for {Type} after {Attempts} attempt(s); policy chose to discard.",
                            typeFullName, attempt);
                        return;

                    default:
                        // Unknown kind: a buggy policy. Escalate AND defensively dead-letter so the
                        // message isn't silently lost.
                        logger.LogError(ex, "Inbound dispatch failed for {Type}; recoverability policy returned unknown action {Kind}; dead-lettering and raising critical error.",
                            typeFullName, decision.Kind);
                        await SafeNotifyCriticalAsync(criticalErrorHandler,
                            new CriticalErrorContext(
                                $"Recoverability policy returned unknown action '{decision.Kind}' for {typeFullName}; dispatch was not recovered.",
                                Exception: ex, MessageType: typeFullName, Headers: headers),
                            logger, cancellationToken).ConfigureAwait(false);
                        await SafeWriteToDeadLetterAsync(deadLetterStore, criticalErrorHandler, typeFullName, headers, payload, ex, logger, cancellationToken)
                            .ConfigureAwait(false);
                        return;
                }
            }
        }
    }

    /// <summary>
    /// Write to the dead-letter store; if the DLQ itself throws (the system can't even capture the
    /// failure), escalate via the critical-error handler. The original handler exception is
    /// preserved as the inner exception of the critical-error context.
    /// </summary>
    private static async System.Threading.Tasks.Task SafeWriteToDeadLetterAsync(
        IDeadLetterStore deadLetterStore,
        ICriticalErrorHandler criticalErrorHandler,
        string typeFullName,
        MessageHeaders headers,
        System.Buffers.ReadOnlySequence<byte> payload,
        System.Exception originalException,
        ILogger logger,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            await deadLetterStore.WriteAsync(typeFullName, payload, originalException.Message, originalException.ToString(), cancellationToken).ConfigureAwait(false);
        }
        catch (System.Exception dlqEx)
        {
            logger.LogCritical(dlqEx, "Mosaic dead-letter store failed while persisting {Type}; the message has been LOST.", typeFullName);
            // Aggregate keeps both the original handler failure AND the DLQ-store failure visible
            // — operators need both to triage ("what failed → why couldn't we capture it").
            var aggregate = new System.AggregateException(
                $"Dead-letter write failed for {typeFullName}; message was lost.",
                dlqEx, originalException);
            await SafeNotifyCriticalAsync(criticalErrorHandler,
                new CriticalErrorContext(
                    $"Dead-letter store unavailable; lost message {typeFullName}.",
                    Exception: aggregate, MessageType: typeFullName, Headers: headers),
                logger, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Invoke the critical-error handler with belt-and-braces: a thrown handler must not escalate
    /// into a second critical error or interrupt the dispatch loop. Logs the failure instead.
    /// </summary>
    private static async System.Threading.Tasks.Task SafeNotifyCriticalAsync(
        ICriticalErrorHandler handler,
        CriticalErrorContext context,
        ILogger logger,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Mosaic critical-error handler threw while reporting: {Message}", context.Message);
        }
    }
}
