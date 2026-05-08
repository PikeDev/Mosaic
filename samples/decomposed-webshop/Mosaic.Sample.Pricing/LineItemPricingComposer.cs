using Mosaic.Sample.Composition;

namespace Mosaic.Sample.Pricing;

// Pricing is the technical authority for prices. It contributes UnitPrice — and only UnitPrice.
internal static class PricingStore
{
    public static readonly Dictionary<int, decimal> Prices = new()
    {
        [42] = 19.99m,
        [17] = 89.50m,
        [99] = 4.25m,
    };
}

public sealed class LineItemPricingComposer : IComposer<GetLineItem, LineItemVm>
{
    public ValueTask Compose(GetLineItem request, LineItemVm viewModel, ICompositionContext context, CancellationToken cancellationToken)
    {
        viewModel.UnitPrice = PricingStore.Prices.GetValueOrDefault(request.ProductId);
        return default;
    }
}
