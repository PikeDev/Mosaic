namespace Mosaic;

/// <summary>
/// Informational annotation declaring that a view-model property (or sub-object) is owned by a
/// specific composer type. Used by tooling, code review, and documentation to make per-service
/// slice ownership explicit on the view-model itself — the framework does not enforce ownership
/// at runtime; composers are free to write any field.
/// </summary>
/// <example>
/// <code>
/// public sealed class ProductDetailPageViewModel
/// {
///     [OwnedBy(typeof(CatalogContributor))]
///     public ProductCatalogSection Catalog { get; } = new();
///
///     [OwnedBy(typeof(PriceContributor))]
///     public ProductPriceSection Price { get; } = new();
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
public sealed class OwnedByAttribute(Type composerType) : Attribute
{
    public Type ComposerType { get; } = composerType;
}
