using Mosaic.Sample.Composition;

namespace Mosaic.Sample.Catalog;

// Catalog is the technical authority for product information. It contributes Title (and ProductId).
// It knows nothing about prices or stock — those live in Pricing and Inventory.
internal static class CatalogStore
{
    public static readonly Dictionary<int, string> Titles = new()
    {
        [42] = "Acme Widget",
        [17] = "Premium Gizmo",
        [99] = "Bulk Sprocket",
    };
}

public sealed class LineItemCatalogComposer : IComposer<GetLineItem, LineItemVm>
{
    public ValueTask Compose(GetLineItem request, LineItemVm viewModel, ICompositionContext context, CancellationToken cancellationToken)
    {
        viewModel.ProductId = request.ProductId;
        viewModel.Title = CatalogStore.Titles.GetValueOrDefault(request.ProductId, "Unknown product");
        return default;
    }
}
