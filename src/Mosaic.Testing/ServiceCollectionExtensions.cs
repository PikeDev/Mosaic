using Microsoft.Extensions.DependencyInjection;

namespace Mosaic.Testing;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="MosaicTestRecorder"/> singleton plus the recording pipeline /
    /// publish / compose behaviors. Call once in your test composition. The recording behaviors
    /// must additionally be listed in your test assembly's
    /// <c>[CompositionConfiguration(PipelineBehaviors / PublishBehaviors / ComposeBehaviors = …)]</c>
    /// so the source generator picks them up — the harness handles the DI side; you handle the
    /// attribute side once per test assembly. See the package README for the exact snippet.
    /// </summary>
    public static IServiceCollection AddMosaicTestHarness(this IServiceCollection services)
    {
        services.AddSingleton<MosaicTestRecorder>();
        // Open-generic registrations — the source-generated dispatcher resolves them as closed
        // generics per (TRequest, TResponse) / TEvent / (TRequest, TViewModel) at dispatch time.
        services.AddScoped(typeof(RecordingPipelineBehavior<,>));
        services.AddScoped(typeof(RecordingPublishBehavior<>));
        services.AddScoped(typeof(RecordingComposeBehavior<,>));
        return services;
    }
}
