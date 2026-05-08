using Microsoft.Extensions.DependencyInjection;

namespace Mosaic;

/// <summary>
/// Sets the DI lifetime of a handler, composer, event handler, or pipeline behavior.
/// Applied at type level. Overrides the assembly-level default set on
/// <see cref="CompositionConfigurationAttribute.DefaultLifetime"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LifetimeAttribute(ServiceLifetime lifetime) : Attribute
{
    public ServiceLifetime Lifetime { get; } = lifetime;
}
