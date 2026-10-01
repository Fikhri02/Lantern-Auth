namespace Lantern.Till.Components.Till;

/// <summary>The till's product buttons. Prices come back from the API on the receipt.</summary>
public static class TillCatalog
{
    public sealed record Item(string Sku, string Name);

    public static readonly IReadOnlyList<Item> Items =
    [
        new("SKU-100", "House Blend 1kg"),
        new("SKU-200", "Oat Milk 1L")
    ];

    public static string NameOf(string sku) => Items.FirstOrDefault(i => i.Sku == sku)?.Name ?? sku;
}
