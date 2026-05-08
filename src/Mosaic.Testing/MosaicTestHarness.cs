using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mosaic.Testing;

/// <summary>
/// Test harness that builds a full Mosaic engine + DI container, starts hosted services, and
/// exposes recording facades for every <c>Send</c>, <c>Compose</c>, and <c>Publish</c> that
/// passes through. Implements <see cref="IAsyncDisposable"/>; tear down at the end of the test
/// so hosted services stop cleanly.
/// <para>
/// Construction is synchronous; hosted services start in the background and are stopped on
/// dispose. Pair with the <see cref="ServiceCollectionExtensions.AddMosaicTestHarness"/> call
/// inside your test composition + the recording-behavior wiring documented in the package README.
/// </para>
/// </summary>
public sealed class MosaicTestHarness : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _disposed;

    private MosaicTestHarness(IHost host)
    {
        _host = host;
        Services = host.Services;
        Engine = Services.GetRequiredService<ICompositionEngine>();
        Recorder = Services.GetRequiredService<MosaicTestRecorder>();
    }

    public IServiceProvider Services { get; }

    /// <summary>The composition engine — call <see cref="ICompositionEngine.Send"/>, <c>.Compose</c>,
    /// <c>.Publish</c> directly. The recording behaviors observe every dispatch.</summary>
    public ICompositionEngine Engine { get; }

    /// <summary>The recorder — typed assertion facades for every recorded category.</summary>
    public MosaicTestRecorder Recorder { get; }

    /// <summary>Shorthand: <c>harness.Sent&lt;PlaceOrder&gt;()</c>.</summary>
    public RecordedMessages<TRequest> Sent<TRequest>() => Recorder.Sent<TRequest>();

    /// <summary>Shorthand: <c>harness.Published&lt;OrderAccepted&gt;()</c>.</summary>
    public RecordedMessages<TEvent> Published<TEvent>() where TEvent : IEvent => Recorder.Published<TEvent>();

    /// <summary>Shorthand: <c>harness.Composed&lt;GetProductTile&gt;()</c> — captures the request
    /// passed in. For the populated view-model snapshot, see <see cref="ComposedResults{TViewModel}"/>.</summary>
    public RecordedMessages<TRequest> Composed<TRequest>() => Recorder.Composed<TRequest>();

    /// <summary>The view-model after the compose chain ran (post-population).</summary>
    public RecordedMessages<TViewModel> ComposedResults<TViewModel>() => Recorder.ComposedResults<TViewModel>();

    /// <summary>
    /// Build a harness from a configuration callback. Call <see cref="ServiceCollectionExtensions.AddMosaicTestHarness"/>
    /// inside the callback (or it'll throw at first dispatch when the recorder isn't found). The
    /// <c>AddMosaic()</c> the source generator emits is also expected to be called inside this
    /// callback — usually by transitive registration extensions like <c>AddSales(...)</c> in the
    /// webshop sample, which call into <c>AddMosaic()</c>.
    /// </summary>
    public static async Task<MosaicTestHarness> CreateAsync(
        Action<IServiceCollection> configure,
        CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        configure(builder.Services);
        var host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return new MosaicTestHarness(host);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        if (_host is IAsyncDisposable a) await a.DisposeAsync().ConfigureAwait(false);
        else _host.Dispose();
    }
}
