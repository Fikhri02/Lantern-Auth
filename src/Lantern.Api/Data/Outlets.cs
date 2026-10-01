namespace Lantern.Api.Data;

/// <summary>Outlet id ↔ Keycloak group, and the naming used for its till accounts and cashier codes.</summary>
public sealed record Outlet(string Id, string Name, string GroupPath, string Slug, char CashierDigit);

public static class Outlets
{
    public static readonly IReadOnlyList<Outlet> All =
    [
        new("BGS", "Bangsar", "/Outlets/Bangsar", "bangsar", '1'),
        new("KLC", "KLCC", "/Outlets/KLCC", "klcc", '2'),
        new("PJY", "PJ", "/Outlets/PJ", "pj", '3')
    ];

    public static Outlet? Find(string id) =>
        All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
}
