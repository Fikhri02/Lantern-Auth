using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Auth;

/// <summary>User must hold <see cref="Role"/> and belong to the outlet named in the route; hq-admin bypasses.</summary>
public sealed record OutletRequirement(string Role) : IAuthorizationRequirement;

public sealed class OutletRequirementHandler : AuthorizationHandler<OutletRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OutletRequirement requirement)
    {
        if (context.Resource is not HttpContext http) return Task.CompletedTask;
        if (http.GetRouteValue("outletId") is not string routeOutlet || routeOutlet.Length == 0) return Task.CompletedTask;

        if (context.User.IsInRole(Roles.HqAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userOutlet = context.User.FindFirstValue("outlet_id");
        if (context.User.IsInRole(requirement.Role) &&
            string.Equals(userOutlet, routeOutlet, StringComparison.OrdinalIgnoreCase))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
