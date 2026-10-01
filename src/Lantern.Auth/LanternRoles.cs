using System.Security.Claims;

namespace Lantern.Auth;

public static class LanternRoles
{
    public const string HqAdmin = "hq-admin";
    public const string HqStaff = "hq-staff";
    public const string Marketing = "marketing";
    public const string Procurement = "procurement";
    public const string Finance = "finance";
    public const string OutletManager = "outlet-manager";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>
    {
        HqAdmin, HqStaff, Marketing, Procurement, Finance, OutletManager, "outlet-device", "cashier"
    };

    public static string[] Of(ClaimsPrincipal user) =>
        user.FindAll("roles").Select(c => c.Value).Where(Known.Contains).Order().ToArray();

    public static string? Sid(ClaimsPrincipal user) => user.FindFirstValue("sid");
}
