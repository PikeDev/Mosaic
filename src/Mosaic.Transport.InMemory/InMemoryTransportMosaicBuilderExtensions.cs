using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mosaic.Runtime;

namespace Mosaic.Transport.InMemory;

public static class InMemoryTransportMosaicBuilderExtensions
{
    /// <summary>
    /// Configures Mosaic's <see cref="IEventTransport"/> to use the in-memory transport — events
    /// published in this container are routed to peer containers in the same process that share
    /// the same <paramref name="channelName"/>.
    /// <para>
    /// Usage (test):
    /// <code>
    /// var publisher  = new ServiceCollection().AddMosaic().UseInMemoryTransport().Services.BuildServiceProvider();
    /// var subscriber = new ServiceCollection().AddMosaic().UseInMemoryTransport().Services.BuildServiceProvider();
    /// </code>
    /// </para>
    /// </summary>
    public static IMosaicBuilder UseInMemoryTransport(this IMosaicBuilder builder, string? channelName = null)
    {
        var services = builder.Services;
        services.AddSingleton<IEventTransport>(sp => new InMemoryEventTransport(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IDeadLetterStore>(),
            sp.GetRequiredService<IRecoverabilityPolicy>(),
            sp.GetRequiredService<ICriticalErrorHandler>(),
            sp.GetRequiredService<ILogger<InMemoryEventTransport>>(),
            channelName));
        return builder;
    }
}
