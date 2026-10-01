using Lantern.Api.Auth;
using Lantern.Api.Data;
using Lantern.Api.Keycloak;

namespace Lantern.Api.Endpoints;

public sealed record NewCashier(string? OutletId, string? FirstName, string? LastName);

public static class CashierEndpoints
{
    public static void MapCashierEndpoints(this IEndpointRouteBuilder app)
    {
        var cashiers = app.MapGroup("/cashiers").RequireAuthorization(Policies.StaffAdmin);

        cashiers.MapPost("/", async (NewCashier body, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var outlet = body.OutletId is null ? null : Outlets.Find(body.OutletId);
            if (outlet is null) errors["outletId"] = ["Unknown outlet."];
            if (string.IsNullOrWhiteSpace(body.FirstName)) errors["firstName"] = ["First name is required."];
            if (string.IsNullOrWhiteSpace(body.LastName)) errors["lastName"] = ["Last name is required."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var temporaryPin = PinGenerator.NewTemporaryPin();
            var created = await kc.CreateCashierAsync(outlet!, body.FirstName!.Trim(), body.LastName!.Trim(), temporaryPin, ct);
            if (created.Conflict)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "That cashier code was just taken; try again.");
            if (created.Errors is not null) return Results.ValidationProblem(created.Errors);
            return Results.Created($"/cashiers/{created.Id}", new { id = created.Id, code = created.Username, temporaryPin });
        });

        cashiers.MapPost("/{id}/reset-pin", async (string id, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            if (await kc.GetUserAsync(id, ct) is null)
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Cashier not found.");
            if (!await kc.HasRealmRoleAsync(id, Roles.Cashier, ct))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Not a cashier.");

            var temporaryPin = PinGenerator.NewTemporaryPin();
            await kc.SetPinAsync(id, temporaryPin, temporary: true, ct);
            return Results.Ok(new { temporaryPin });
        });
    }
}
