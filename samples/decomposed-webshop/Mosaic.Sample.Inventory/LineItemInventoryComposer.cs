using Mosaic.Sample.Composition;

namespace Mosaic.Sample.Inventory;

// Inventory contributes its slice of LineItemVm — InStock + AvailableUnits — and nothing else.
public sealed class LineItemInventoryComposer : IComposer<GetLineItem, LineItemVm>
{
    public ValueTask Compose(GetLineItem request, LineItemVm viewModel, ICompositionContext context, CancellationToken cancellationToken)
    {
        var available = InventoryStore.GetStock(request.ProductId);
        viewModel.AvailableUnits = available;
        viewModel.InStock = available > 0;
        return default;
    }
}
