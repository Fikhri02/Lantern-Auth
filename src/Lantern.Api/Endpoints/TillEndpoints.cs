using Lantern.Api.Auth;
using Lantern.Api.Data;
using Lantern.Api.Keycloak;

namespace Lantern.Api.Endpoints;

public sealed record NewTill(string? Password);

public static class TillEndpoints
{
    public static void MapTillEndpoints(this IEndpointRouteBuilder app)
    {
        var tills = app.MapGroup("/outlets/{outletId}/tills");

        tills.MapGet("/", async (string outletId, KeycloakAdminClient kc, CancellationToken ct) =>
                Outlets.Find(outletId) is { } outlet ? Results.Ok(await kc.ListTillsAsync(outlet, ct)) : OutletNotFound())
            .RequireAuthorization(Policies.OutletManage);

        tills.MapPost("/", async (string outletId, NewTill body, KeycloakAdminClient kc, CancellationToken ct) =>
            {
                if (Outlets.Find(outletId) is not { } outlet) return OutletNotFound();
                if (body.Password is null || body.Password.Length < 8)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Use at least 8 characters."] });

                var created = await kc.CreateTillAccountAsync(outlet, body.Password, ct);
                if (created.Conflict)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "That till name was just taken; try again.");
                if (created.Errors is not null) return Results.ValidationProblem(created.Errors);
                return Results.Created($"/outlets/{outlet.Id}/tills/{created.Id}", new { id = created.Id, username = created.Username });
            })
            .RequireAuthorization(Policies.StaffAdmin);

        tills.MapDelete("/{userId}/session", async (string outletId, string userId, KeycloakAdminClient kc, CancellationToken ct) =>
            {
                if (Outlets.Find(outletId) is not { } outlet) return OutletNotFound();
                var outletTills = await kc.GetOutletMembersWithRoleAsync(outlet, Roles.OutletDevice, ct);
                if (outletTills.All(t => t.Id != userId))
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Till not found in this outlet.");

                return await kc.ReleaseTillAsync(userId, ct)
                    ? Results.NoContent()
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Till is not signed in.");
            })
            .RequireAuthorization(Policies.OutletManage);
    }

    private static IResult OutletNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Outlet not found.");
}
