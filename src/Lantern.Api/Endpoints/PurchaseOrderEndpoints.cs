using System.Security.Claims;
using Lantern.Api.Auth;
using Lantern.Api.Data;
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Endpoints;

public sealed record NewPurchaseOrder(string? SupplierId, decimal Amount);

public static class PurchaseOrderEndpoints
{
    public static void MapPurchaseOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/purchase-orders");

        orders.MapGet("/", (DemoStore store) => Results.Ok(store.PurchaseOrders))
            .RequireAuthorization(Policies.PurchaseOrdersRead);

        orders.MapPost("/", (NewPurchaseOrder body, ClaimsPrincipal user, DemoStore store) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (store.Suppliers.All(s => s.Id != body.SupplierId)) errors["supplierId"] = ["Unknown supplier."];
                if (body.Amount <= 0) errors["amount"] = ["Amount must be greater than zero."];
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                var po = store.AddPurchaseOrder(body.SupplierId!, body.Amount, user.GetSub(), user.GetDisplayName());
                return Results.Created($"/purchase-orders/{po.Id}", po);
            })
            .RequireAuthorization(Policies.Procurement);

        orders.MapPost("/{id:guid}/approve", async (Guid id, ClaimsPrincipal user, DemoStore store, IAuthorizationService auth) =>
            {
                var po = store.FindPurchaseOrder(id);
                if (po is null)
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Purchase order not found.");

                var check = await auth.AuthorizeAsync(user, po, Policies.FinanceApprove);
                if (!check.Succeeded)
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Forbidden",
                        detail: "You can't approve a purchase order you raised.",
                        extensions: new Dictionary<string, object?> { ["policy"] = Policies.FinanceApprove });

                var approved = store.TryApprove(id, user.GetDisplayName());
                return approved is null
                    ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Already decided",
                        detail: "This purchase order has already been approved.")
                    : Results.Ok(approved);
            })
            .RequireAuthorization(Policies.FinanceRole);
    }
}
