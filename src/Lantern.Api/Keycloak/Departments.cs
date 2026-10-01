namespace Lantern.Api.Keycloak;

/// <summary>HQ departments are Keycloak groups; the group grants the roles (spec §4.3).</summary>
public static class Departments
{
    public static readonly IReadOnlyDictionary<string, string> Paths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = "/HQ/Admin",
            ["Marketing"] = "/HQ/Marketing",
            ["Procurement"] = "/HQ/Procurement",
            ["Finance"] = "/HQ/Finance"
        };

    public static bool IsValid(string department) => Paths.ContainsKey(department);
    public static string ToGroupPath(string department) => Paths[department];
    public static bool IsDepartmentPath(string path) => Paths.Values.Contains(path);
}
