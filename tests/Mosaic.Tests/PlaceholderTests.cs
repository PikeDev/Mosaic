using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Mosaic.Tests;

public class AbstractionsCompileSmokeTests
{
    [Fact]
    public void IRequest_marker_is_present()
    {
        // Trivial smoke test confirming the abstractions assembly loads and the marker exists.
        var iface = typeof(IRequest<>);
        iface.IsInterface.ShouldBeTrue();
    }

    [Fact]
    public void IComposable_marker_is_present()
    {
        typeof(IComposable<>).IsInterface.ShouldBeTrue();
    }

    [Fact]
    public void IEvent_marker_is_present()
    {
        typeof(IEvent).IsInterface.ShouldBeTrue();
    }

    [Fact]
    public void LifetimeAttribute_carries_ServiceLifetime()
    {
        var attr = new LifetimeAttribute(ServiceLifetime.Singleton);
        attr.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }
}
