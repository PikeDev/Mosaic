using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mosaic.Outbox.EFCore;

public static class EFCoreSchedulingMosaicBuilderExtensions
{
    /// <summary>
    /// Configures Mosaic to persist scheduled messages into <typeparamref name="TDbContext"/>'s
    /// change tracker at <see cref="Mosaic.ICompositionContext.ScheduleMessage"/> time. The next
    /// <c>SaveChangesAsync</c> the consumer calls commits the schedule in the same transaction as
    /// state changes — atomic with the saga progression that produced the timeout.
    /// <para>
    /// A relay hosted service polls due rows and dispatches them via the in-process
    /// <see cref="IInboundEventDispatcher"/>, so the message reaches the same handlers as a
    /// regular <c>Publish</c> would (just at a future time).
    /// </para>
    /// <para>
    /// Consumer side: the <c>.UseMosaicOutbox&lt;TDbContext&gt;()</c> call on the DbContext
    /// registration covers both outbox and scheduling — they share the model customizer.
    /// </para>
    /// </summary>
    public static IMosaicBuilder UseEFCoreScheduling<TDbContext>(this IMosaicBuilder builder, System.Action<EFCoreSchedulingOptions>? configure = null)
        where TDbContext : DbContext
    {
        var services = builder.Services;
        var options = new EFCoreSchedulingOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddScoped<IScheduledMessageStore, EFCoreScheduledMessageStore<TDbContext>>();
        services.AddHostedService<EFCoreScheduledRelayHostedService<TDbContext>>();
        return builder;
    }
}
