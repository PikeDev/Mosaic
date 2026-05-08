namespace Mosaic.Sample.Composition;

// Top-level query: produces a fully composed view-model. Has a single handler (the orchestrator).
public sealed record GetCheckoutSummary(int CustomerId, IReadOnlyList<int> ProductIds)
    : IRequest<CheckoutSummaryVm>;

// Composable: ANY service may register an IComposer<GetLineItem, LineItemVm> to contribute a slice.
// Composers fan out in parallel and merge into the same VM instance.
public sealed record GetLineItem(int ProductId) : IComposable<LineItemVm>;
