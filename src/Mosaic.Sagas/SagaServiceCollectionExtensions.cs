using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Mosaic.Sagas;

public static class SagaServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="SagaProcessor{TState}"/> implementation and ensures the shared
    /// <see cref="MosaicSagaHost"/> background service is wired up. Idempotent in the host —
    /// safe to call once per saga.
    /// </summary>
    public static IServiceCollection AddSaga<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(this IServiceCollection services)
        where TProcessor : class, ISagaProcessor
    {
        services.AddSingleton<ISagaProcessor, TProcessor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MosaicSagaHost>());
        return services;
    }
}
