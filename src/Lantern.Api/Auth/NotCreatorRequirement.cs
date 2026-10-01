using System.Security.Claims;
using Lantern.Api.Data;
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Auth;

/// <summary>Separation of duties: whoever raised a purchase order may not approve it.</summary>
public sealed class NotCreatorRequirement : IAuthorizationRequirement;

public sealed class NotCreatorHandler : AuthorizationHandler<NotCreatorRequirement, PurchaseOrder>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        NotCreatorRequirement requirement, PurchaseOrder resource)
    {
        var sub = context.User.FindFirstValue("sub");
        if (sub is not null && sub != resource.CreatedBySub)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
