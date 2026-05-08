using Microsoft.Extensions.DependencyInjection;

namespace Mosaic.Runtime;

/// <summary>
/// Builder extensions for swapping in a custom <see cref="ICriticalErrorHandler"/>. Replaces the
/// default <see cref="LoggingCriticalErrorHandler"/> registered by <c>AddMosaic()</c>.
/// </summary>
public static class CriticalErrorMosaicBuilderExtensions
{
    /// <summary>Use <typeparamref name="THandler"/> as the critical-error sink (resolved from DI).</summary>
    public static IMosaicBuilder UseCriticalErrorHandler<THandler>(this IMosaicBuilder builder)
        where THandler : class, ICriticalErrorHandler
    {
        ReplaceHandler(builder.Services, ServiceDescriptor.Singleton<ICriticalErrorHandler, THandler>());
        return builder;
    }

    /// <summary>Use a specific handler instance — easiest path for test harness wiring.</summary>
    public static IMosaicBuilder UseCriticalErrorHandler(this IMosaicBuilder builder, ICriticalErrorHandler handler)
    {
        ReplaceHandler(builder.Services, ServiceDescriptor.Singleton(handler));
        return builder;
    }

    /// <summary>
    /// Wire a delegate as the critical-error sink — the lightest path for ad-hoc escalation:
    /// <code>
    /// builder.UseCriticalErrorHandler((ctx, ct) =>
    /// {
    ///     pagerDuty.Trigger(ctx.Message, ctx.Exception);
    ///     return ValueTask.CompletedTask;
    /// });
    /// </code>
    /// </summary>
    public static IMosaicBuilder UseCriticalErrorHandler(
        this IMosaicBuilder builder,
        System.Func<CriticalErrorContext, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask> handle)
        => builder.UseCriticalErrorHandler(new DelegateCriticalErrorHandler(handle));

    private static void ReplaceHandler(IServiceCollection services, ServiceDescriptor replacement)
    {
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(ICriticalErrorHandler))
            {
                services.RemoveAt(i);
            }
        }
        services.Add(replacement);
    }

    private sealed class DelegateCriticalErrorHandler : ICriticalErrorHandler
    {
        private readonly System.Func<CriticalErrorContext, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask> _handle;
        public DelegateCriticalErrorHandler(System.Func<CriticalErrorContext, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask> handle) { _handle = handle; }
        public System.Threading.Tasks.ValueTask HandleAsync(CriticalErrorContext context, System.Threading.CancellationToken cancellationToken = default)
            => _handle(context, cancellationToken);
    }
}
