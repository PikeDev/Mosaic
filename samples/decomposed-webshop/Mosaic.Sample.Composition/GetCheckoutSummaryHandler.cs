namespace Mosaic.Sample.Composition;

// The orchestrating handler — sits in the composition (edge) layer, invoked once per HTTP request.
// Injects ICompositionEngine and uses it to compose each line by fanning out across the
// Catalog/Pricing/Inventory composers. The handler itself owns no domain data — its only job is
// to assemble the response shape.
public sealed class GetCheckoutSummaryHandler(ICompositionEngine engine)
    : IRequestHandler<GetCheckoutSummary, CheckoutSummaryVm>
{
    public async ValueTask<CheckoutSummaryVm> Handle(
        GetCheckoutSummary query,
        ICompositionContext context,
        CancellationToken cancellationToken)
    {
        var summary = new CheckoutSummaryVm { CustomerId = query.CustomerId };

        foreach (var productId in query.ProductIds)
        {
            // engine.Compose dispatches the GetLineItem composable to every IComposer<GetLineItem, LineItemVm>
            // discovered across the solution. Catalog fills Title; Pricing fills UnitPrice;
            // Inventory fills InStock + AvailableUnits — all in parallel against the same VM instance.
            var line = await engine.Compose(new GetLineItem(productId), cancellationToken);
            summary.Lines.Add(line);
        }

        summary.Total = summary.Lines.Sum(l => l.UnitPrice);
        return summary;
    }
}
