using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Auth;

/// <summary>
/// User must hold one of <see cref="AllowedRoles"/> and belong to the outlet named in the route.
/// hq-admin passes only when <see cref="HqAdminBypass"/> is set (reading, yes; ringing sales, no).
/// </summary>
public sealed record OutletRequirement(IReadOnlyList<string> AllowedRoles, bool HqAdminBypass) : IAuthorizationRequirement;

public sealed class OutletRequirementHandler : AuthorizationHandler<OutletRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OutletRequirement requirement)
    {
        if (context.Resource is not HttpContext http) return Task.CompletedTask;
        if (http.GetRouteValue("outletId") is not string routeOutlet || routeOutlet.Length == 0) return Task.CompletedTask;

        if (requirement.HqAdminBypass && context.User.IsInRole(Roles.HqAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userOutlet = context.User.FindFirstValue("outlet_id");
        if (requirement.AllowedRoles.Any(context.User.IsInRole) &&
            string.Equals(userOutlet, routeOutlet, StringComparison.OrdinalIgnoreCase))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
