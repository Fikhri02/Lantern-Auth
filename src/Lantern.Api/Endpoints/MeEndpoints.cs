using System.Security.Claims;
using Lantern.Api.Auth;

namespace Lantern.Api.Endpoints;

public sealed record MeResponse(string Id, string Name, string[] Roles, string? OutletId);

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new MeResponse(
                user.FindFirstValue("sub") ?? "",
                user.Identity?.Name ?? "",
                user.FindAll("roles").Select(c => c.Value).Where(Roles.All.Contains).Order().ToArray(),
                user.FindFirstValue("outlet_id"))))
            .RequireAuthorization();
}
