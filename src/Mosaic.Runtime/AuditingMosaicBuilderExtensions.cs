using Microsoft.Extensions.DependencyInjection;

namespace Mosaic.Runtime;

/// <summary>
/// Opt-in builder extensions for the message audit pipeline. Replaces the default
/// <see cref="NoOpMessageAuditStore"/> with a real <see cref="IMessageAuditStore"/> and starts
/// recording every Sent/Received hop the engine performs.
/// <para>
/// Pair with a long-lived process to use the trail: query <see cref="InMemoryMessageAuditStore.ByCorrelation"/>
/// in tests/demos, or write your own <see cref="IMessageAuditStore"/> that ships rows to your
/// observability stack.
/// </para>
/// </summary>
public static class AuditingMosaicBuilderExtensions
{
    /// <summary>
    /// Register <typeparamref name="TStore"/> as the audit sink. Replaces the default no-op so
    /// the source-generated engine + <see cref="InboundDispatch"/> start writing audit rows.
    /// </summary>
    public static IMosaicBuilder UseAuditing<TStore>(this IMosaicBuilder builder)
        where TStore : class, IMessageAuditStore
    {
        ReplaceAuditStore(builder.Services, ServiceDescriptor.Singleton<IMessageAuditStore, TStore>());
        return builder;
    }

    /// <summary>
    /// Convenience overload for tests + samples: registers the queryable
    /// <see cref="InMemoryMessageAuditStore"/>. Resolve the same instance from DI to
    /// inspect the captured chain via <see cref="InMemoryMessageAuditStore.ByCorrelation"/>.
    /// </summary>
    public static IMosaicBuilder UseInMemoryAuditing(this IMosaicBuilder builder)
    {
        var store = new InMemoryMessageAuditStore();
        ReplaceAuditStore(builder.Services, ServiceDescriptor.Singleton<IMessageAuditStore>(store));
        // Also register the concrete type so callers can resolve `InMemoryMessageAuditStore`
        // directly to query it without a downcast. Same instance — single source of truth.
        builder.Services.AddSingleton(store);
        return builder;
    }

    private static void ReplaceAuditStore(IServiceCollection services, ServiceDescriptor replacement)
    {
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IMessageAuditStore))
            {
                services.RemoveAt(i);
            }
        }
        services.Add(replacement);
    }
}
