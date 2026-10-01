using System.Security.Claims;
using System.Text.RegularExpressions;
using Lantern.Api.Auth;
using Lantern.Api.Keycloak;

namespace Lantern.Api.Endpoints;

public static partial class StaffEndpoints
{
    [GeneratedRegex("^[a-z0-9.\\-]{3,40}$")]
    private static partial Regex UsernamePattern();

    public static void MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/staff").RequireAuthorization(Policies.StaffAdmin);

        staff.MapGet("/", async (KeycloakAdminClient kc, CancellationToken ct) =>
            Results.Ok(await kc.ListStaffAsync(ct)));

        staff.MapPost("/", async (NewStaff body, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            var errors = ValidateNewStaff(body);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var result = await kc.CreateStaffAsync(body, ct);
            if (result.Conflict)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Username or email already exists.");
            if (result.Errors is not null) return Results.ValidationProblem(result.Errors);
            return Results.Created($"/staff/{result.Id}", new { id = result.Id, inviteSent = result.InviteSent });
        });

        staff.MapPut("/{id}/departments", async (string id, DepartmentsUpdate body, ClaimsPrincipal user,
            KeycloakAdminClient kc, CancellationToken ct) =>
        {
            var errors = ValidateDepartments(body.Departments);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (await kc.GetUserAsync(id, ct) is null) return StaffNotFound();
            if (!await kc.IsHqMemberAsync(id, ct))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Only HQ staff have departments.");
            if (id == user.GetSub() && !body.Departments!.Contains("Admin", StringComparer.OrdinalIgnoreCase))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "You can't remove your own Admin department.");

            await kc.SetDepartmentsAsync(id, body.Departments!, ct);
            return Results.NoContent();
        });

        staff.MapPost("/{id}/deactivate", async (string id, ClaimsPrincipal user, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            if (id == user.GetSub())
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "You can't deactivate your own account.");
            var target = await kc.GetUserAsync(id, ct);
            if (target is null) return StaffNotFound();
            if (KeycloakAdminClient.IsServiceAccount(target))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Service accounts can't be deactivated here.");

            await kc.DeactivateAsync(id, ct);
            return Results.NoContent();
        });

        staff.MapPost("/{id}/reactivate", async (string id, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            if (await kc.GetUserAsync(id, ct) is null) return StaffNotFound();
            await kc.ReactivateAsync(id, ct);
            return Results.NoContent();
        });

        staff.MapPost("/{id}/reset-password", async (string id, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            if (await kc.GetUserAsync(id, ct) is null) return StaffNotFound();
            await kc.SendActionsEmailAsync(id, ["UPDATE_PASSWORD"], ct);
            return Results.Accepted();
        });
    }

    private static IResult StaffNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Staff member not found.");

    private static Dictionary<string, string[]> ValidateNewStaff(NewStaff body)
    {
        var errors = ValidateDepartments(body.Departments);
        if (body.Username is null || !UsernamePattern().IsMatch(body.Username))
            errors["username"] = ["Use 3–40 lowercase letters, digits, dots or hyphens."];
        if (string.IsNullOrWhiteSpace(body.FirstName)) errors["firstName"] = ["First name is required."];
        if (string.IsNullOrWhiteSpace(body.LastName)) errors["lastName"] = ["Last name is required."];
        if (string.IsNullOrWhiteSpace(body.Email) || !body.Email.Contains('@')) errors["email"] = ["A valid email is required."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateDepartments(string[]? departments)
    {
        var errors = new Dictionary<string, string[]>();
        if (departments is null || departments.Length == 0)
            errors["departments"] = ["At least one department is required."];
        else if (departments.FirstOrDefault(d => !Departments.IsValid(d)) is { } bad)
            errors["departments"] = [$"Unknown department '{bad}'. Use one of: {string.Join(", ", Departments.Paths.Keys)}."];
        return errors;
    }
}
