using System.Collections.Concurrent;

namespace Lantern.Api.Data;

public sealed record Promotion(Guid Id, string Name, DateOnly StartsOn, string CreatedBy);
public sealed record Supplier(string Id, string Name);
public sealed record StockLine(string Sku, string Name, int Quantity);
public sealed record RosterEntry(string Name, string Role);
public sealed record OutletSales(string OutletId, decimal Total);
public sealed record CatalogItem(string Name, decimal Price);
public sealed record ReceiptLine(string Sku, string Name, int Quantity, decimal UnitPrice, decimal Amount);
public sealed record Receipt(Guid SaleId, string OutletId, string TillAccount, string CashierId, string CashierCode,
    string CashierName, string ServedBy, IReadOnlyList<ReceiptLine> Lines, decimal Total, DateTimeOffset At);
public sealed record PurchaseOrder(Guid Id, string SupplierId, decimal Amount, string CreatedBySub,
    string CreatedByName, string Status, string? ApprovedByName);

/// <summary>In-memory business data, seeded on start. Exists only to make authorization visible.</summary>
public sealed class DemoStore
{
    public static readonly string[] OutletIds = ["BGS", "KLC", "PJY"];

    private readonly ConcurrentDictionary<Guid, Promotion> _promotions = new();

    public IReadOnlyList<Promotion> Promotions => _promotions.Values.OrderBy(p => p.StartsOn).ToList();

    public Promotion AddPromotion(string name, DateOnly startsOn, string createdBy)
    {
        var promotion = new Promotion(Guid.NewGuid(), name, startsOn, createdBy);
        _promotions[promotion.Id] = promotion;
        return promotion;
    }

    public IReadOnlyList<Supplier> Suppliers { get; } =
    [
        new("SUP-01", "Kopi Beans Trading"),
        new("SUP-02", "Fresh Dairy Co"),
        new("SUP-03", "Packaging Plus")
    ];

    public IReadOnlyDictionary<string, IReadOnlyList<StockLine>> Stock { get; } =
        new Dictionary<string, IReadOnlyList<StockLine>>(StringComparer.OrdinalIgnoreCase)
        {
            ["BGS"] = [new("SKU-100", "House Blend 1kg", 42), new("SKU-200", "Oat Milk 1L", 18)],
            ["KLC"] = [new("SKU-100", "House Blend 1kg", 30), new("SKU-200", "Oat Milk 1L", 25)],
            ["PJY"] = [new("SKU-100", "House Blend 1kg", 12), new("SKU-200", "Oat Milk 1L", 7)]
        };

    public IReadOnlyDictionary<string, IReadOnlyList<RosterEntry>> Roster { get; } =
        new Dictionary<string, IReadOnlyList<RosterEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            ["BGS"] = [new("Siti Aminah", "cashier"), new("Raj Kumar", "cashier")],
            ["KLC"] = [new("Mei Ling Chan", "cashier"), new("Daniel Lee", "cashier")],
            ["PJY"] = [new("Nurul Huda", "cashier")]
        };

    public IReadOnlyList<OutletSales> SalesReport { get; } =
        [new("BGS", 18250.40m), new("KLC", 22410.00m), new("PJY", 9120.75m)];

    public IReadOnlyDictionary<string, CatalogItem> Catalog { get; } =
        new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["SKU-100"] = new("House Blend 1kg", 45.00m),
            ["SKU-200"] = new("Oat Milk 1L", 9.50m)
        };

    private readonly ConcurrentDictionary<Guid, Receipt> _receipts = new();

    public Receipt RecordSale(string outletId, string tillAccount, string cashierId, string cashierCode, string cashierName,
        IReadOnlyList<(string Sku, int Quantity)> items)
    {
        var lines = items.Select(i =>
        {
            var item = Catalog[i.Sku];
            return new ReceiptLine(i.Sku.ToUpperInvariant(), item.Name, i.Quantity, item.Price, item.Price * i.Quantity);
        }).ToList();
        var receipt = new Receipt(Guid.NewGuid(), outletId, tillAccount, cashierId, cashierCode, cashierName,
            $"{cashierName} ({cashierCode})", lines, lines.Sum(l => l.Amount), DateTimeOffset.UtcNow);
        _receipts[receipt.SaleId] = receipt;
        return receipt;
    }

    public Receipt? FindReceipt(string outletId, Guid saleId) =>
        _receipts.TryGetValue(saleId, out var receipt) &&
        string.Equals(receipt.OutletId, outletId, StringComparison.OrdinalIgnoreCase)
            ? receipt
            : null;

    private readonly ConcurrentDictionary<Guid, PurchaseOrder> _orders = new();

    public IReadOnlyList<PurchaseOrder> PurchaseOrders => _orders.Values.ToList();

    public PurchaseOrder AddPurchaseOrder(string supplierId, decimal amount, string createdBySub, string createdByName)
    {
        var po = new PurchaseOrder(Guid.NewGuid(), supplierId, amount, createdBySub, createdByName, "Pending", null);
        _orders[po.Id] = po;
        return po;
    }

    public PurchaseOrder? FindPurchaseOrder(Guid id) => _orders.GetValueOrDefault(id);

    /// <summary>Atomic Pending → Approved. Returns null if the order is missing or already decided.</summary>
    public PurchaseOrder? TryApprove(Guid id, string approverName)
    {
        while (_orders.TryGetValue(id, out var current))
        {
            if (current.Status != "Pending") return null;
            var approved = current with { Status = "Approved", ApprovedByName = approverName };
            if (_orders.TryUpdate(id, approved, current)) return approved;
        }
        return null;
    }
}
