namespace Lantern.Api.Auth;

public static class Roles
{
    public const string HqAdmin = "hq-admin";
    public const string HqStaff = "hq-staff";
    public const string Marketing = "marketing";
    public const string Procurement = "procurement";
    public const string Finance = "finance";
    public const string OutletManager = "outlet-manager";
    public const string OutletDevice = "outlet-device";
    public const string Cashier = "cashier";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        HqAdmin, HqStaff, Marketing, Procurement, Finance, OutletManager, OutletDevice, Cashier
    };
}
