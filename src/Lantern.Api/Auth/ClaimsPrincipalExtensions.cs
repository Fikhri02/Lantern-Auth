using System.Security.Claims;

namespace Lantern.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string GetSub(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("Token has no sub claim.");

    public static string GetDisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue("name") ?? user.Identity?.Name ?? "unknown";
}
