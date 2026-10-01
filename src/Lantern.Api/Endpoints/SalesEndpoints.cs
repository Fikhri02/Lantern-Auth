using System.Security.Claims;
using Lantern.Api.Auth;
using Lantern.Api.Data;

namespace Lantern.Api.Endpoints;

public sealed record SaleLine(string? Sku, int Quantity);
public sealed record NewSale(SaleLine[]? Items);

public static class SalesEndpoints
{
    public static void MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var sales = app.MapGroup("/outlets/{outletId}/sales");

        sales.MapPost("/", (string outletId, NewSale body, ClaimsPrincipal user, DemoStore store) =>
            {
                var errors = Validate(body, store);
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                var till = user.FindFirstValue("till_account");
                if (string.IsNullOrEmpty(till))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Sales must come from a till.");

                var receipt = store.RecordSale(outletId.ToUpperInvariant(), till, user.GetSub(),
                    user.Identity?.Name ?? "", user.GetDisplayName(),
                    body.Items!.Select(i => (i.Sku!, i.Quantity)).ToList());
                return Results.Created($"/outlets/{receipt.OutletId}/sales/{receipt.SaleId}/receipt", receipt);
            })
            .RequireAuthorization(Policies.OutletSell);

        sales.MapGet("/{saleId:guid}/receipt", (string outletId, Guid saleId, DemoStore store) =>
                store.FindReceipt(outletId, saleId) is { } receipt
                    ? Results.Ok(receipt)
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Sale not found."))
            .RequireAuthorization(Policies.OutletReceipts);
    }

    private static Dictionary<string, string[]> Validate(NewSale body, DemoStore store)
    {
        var errors = new Dictionary<string, string[]>();
        if (body.Items is null || body.Items.Length == 0)
            errors["items"] = ["A sale needs at least one item."];
        else if (body.Items.Any(i => i.Sku is null || !store.Catalog.ContainsKey(i.Sku)))
            errors["items"] = ["Unknown SKU."];
        else if (body.Items.Any(i => i.Quantity is < 1 or > 999))
            errors["items"] = ["Quantity must be between 1 and 999."];
        return errors;
    }
}
