using Microsoft.Extensions.DependencyInjection;

namespace Mosaic.Runtime;

/// <summary>
/// Builder extensions for swapping in a custom <see cref="IRecoverabilityPolicy"/>. Replaces the
/// default <see cref="DefaultRecoverabilityPolicy"/> registered by <c>AddMosaic()</c>.
/// <para>
/// Registration ordering matters because the engine resolves a single
/// <see cref="IRecoverabilityPolicy"/>: call <c>UseRecoverability</c> AFTER <c>AddMosaic()</c>
/// (the chained extension does this naturally).
/// </para>
/// </summary>
public static class RecoverabilityMosaicBuilderExtensions
{
    /// <summary>Use <typeparamref name="TPolicy"/> as the recoverability policy (resolved from DI).</summary>
    public static IMosaicBuilder UseRecoverability<TPolicy>(this IMosaicBuilder builder)
        where TPolicy : class, IRecoverabilityPolicy
    {
        ReplacePolicy(builder.Services, ServiceDescriptor.Singleton<IRecoverabilityPolicy, TPolicy>());
        return builder;
    }

    /// <summary>Use a specific <paramref name="policy"/> instance — handy for lambda-driven policies in tests.</summary>
    public static IMosaicBuilder UseRecoverability(this IMosaicBuilder builder, IRecoverabilityPolicy policy)
    {
        ReplacePolicy(builder.Services, ServiceDescriptor.Singleton(policy));
        return builder;
    }

    /// <summary>
    /// Decide via a delegate — the lightest wiring for ad-hoc rules:
    /// <code>
    /// builder.UseRecoverability(ctx => ctx.Exception is TimeoutException
    ///     ? RecoverabilityAction.Retry(TimeSpan.FromSeconds(1))
    ///     : RecoverabilityAction.DeadLetter);
    /// </code>
    /// </summary>
    public static IMosaicBuilder UseRecoverability(this IMosaicBuilder builder, System.Func<RecoverabilityContext, RecoverabilityAction> decide)
        => builder.UseRecoverability(new DelegateRecoverabilityPolicy(decide));

    private static void ReplacePolicy(IServiceCollection services, ServiceDescriptor replacement)
    {
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IRecoverabilityPolicy))
            {
                services.RemoveAt(i);
            }
        }
        services.Add(replacement);
    }

    private sealed class DelegateRecoverabilityPolicy : IRecoverabilityPolicy
    {
        private readonly System.Func<RecoverabilityContext, RecoverabilityAction> _decide;
        public DelegateRecoverabilityPolicy(System.Func<RecoverabilityContext, RecoverabilityAction> decide) { _decide = decide; }
        public RecoverabilityAction Decide(RecoverabilityContext context) => _decide(context);
    }
}
