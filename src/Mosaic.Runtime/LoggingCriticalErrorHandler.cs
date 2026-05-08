using Microsoft.Extensions.Logging;

namespace Mosaic.Runtime;

/// <summary>
/// Default <see cref="ICriticalErrorHandler"/>: emits a <c>Critical</c> log line and returns.
/// Replace with <c>UseCriticalErrorHandler&lt;TYours&gt;()</c> when the deployment needs real
/// escalation (ops paging, Slack, endpoint shutdown).
/// </summary>
public sealed class LoggingCriticalErrorHandler : ICriticalErrorHandler
{
    private readonly ILogger<LoggingCriticalErrorHandler> _logger;

    public LoggingCriticalErrorHandler(ILogger<LoggingCriticalErrorHandler> logger)
    {
        _logger = logger;
    }

    public System.Threading.Tasks.ValueTask HandleAsync(
        CriticalErrorContext context,
        System.Threading.CancellationToken cancellationToken = default)
    {
        _logger.LogCritical(
            context.Exception,
            "Mosaic critical error: {Message} (messageType={MessageType}, correlationId={CorrelationId})",
            context.Message,
            context.MessageType ?? "(none)",
            context.Headers?.CorrelationId ?? "(none)");
        return default;
    }
}
