using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mosaic.Sagas;

/// <summary>
/// Single hosted background service that drives every registered <see cref="ISagaProcessor"/>.
/// Each processor runs in its own loop on its own cadence; one shared error-logging convention.
/// Failures inside a poll cycle are logged and swallowed so the loop survives transient errors.
/// </summary>
internal sealed class MosaicSagaHost(
    IServiceProvider rootServices,
    IEnumerable<ISagaProcessor> processors,
    ILogger<MosaicSagaHost> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stop)
    {
        var procs = processors.ToList();
        if (procs.Count == 0)
        {
            logger.LogInformation("MosaicSagaHost: no saga processors registered.");
            return Task.CompletedTask;
        }

        logger.LogInformation("MosaicSagaHost: driving {Count} saga processor(s): {Names}.",
            procs.Count, string.Join(", ", procs.Select(p => p.Name)));

        var loops = procs.Select(p => RunLoop(p, stop)).ToArray();
        return Task.WhenAll(loops);
    }

    private async Task RunLoop(ISagaProcessor processor, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = rootServices.CreateAsyncScope();
                await processor.ProcessOnceAsync(scope.ServiceProvider, stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "{Saga}: poll cycle failed; will retry.", processor.Name);
            }

            try { await Task.Delay(processor.PollInterval, stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }
}
