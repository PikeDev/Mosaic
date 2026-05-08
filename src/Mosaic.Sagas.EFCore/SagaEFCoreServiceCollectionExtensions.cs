using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mosaic.Sagas;

namespace Mosaic.Sagas.EFCore;

public static class SagaEFCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EFCoreSagaStateStore{TDbContext, TData}"/> as the
    /// <see cref="ISagaStateStore{TData}"/> for <typeparamref name="TData"/>. Source-generated
    /// <c>AddMosaic()</c> calls this automatically for each saga whose primary constructor
    /// accepts a DbContext — manual registration is only needed for sagas without that hint
    /// (e.g. a saga with a custom store).
    /// </summary>
    public static IServiceCollection AddEFCoreSagaState<TDbContext,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.NonPublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties
            | DynamicallyAccessedMemberTypes.NonPublicProperties
            | DynamicallyAccessedMemberTypes.Interfaces)]
        TData>(this IServiceCollection services)
        where TDbContext : DbContext
        where TData : SagaData, new()
    {
        services.AddScoped<ISagaStateStore<TData>, EFCoreSagaStateStore<TDbContext, TData>>();
        return services;
    }
}
