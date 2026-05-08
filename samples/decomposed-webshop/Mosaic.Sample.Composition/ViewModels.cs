namespace Mosaic.Sample.Composition;

// The composition (edge) layer owns the SHAPE of the response. Each property is filled by the
// service that's the technical authority for that piece of data — Catalog for Title, Pricing for
// UnitPrice, Inventory for stock. The composers in those services don't know about each other.
public sealed class LineItemVm
{
    public int ProductId { get; set; }
    public string Title { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public bool InStock { get; set; }
    public int AvailableUnits { get; set; }
}

public sealed class CheckoutSummaryVm
{
    public int CustomerId { get; set; }
    public List<LineItemVm> Lines { get; } = [];
    public decimal Total { get; set; }
}
