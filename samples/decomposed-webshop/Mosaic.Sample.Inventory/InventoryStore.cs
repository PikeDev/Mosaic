namespace Mosaic.Sample.Inventory;

internal static class InventoryStore
{
    private static readonly Dictionary<int, int> _stock = new()
    {
        [42] = 12,
        [17] = 3,
        [99] = 0,
    };

    public static int GetStock(int productId) => _stock.GetValueOrDefault(productId);

    public static void Reserve(int productId, int quantity)
    {
        if (_stock.TryGetValue(productId, out var current))
        {
            _stock[productId] = Math.Max(0, current - quantity);
        }
    }
}
