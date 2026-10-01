using System.Security.Claims;
using Lantern.Api.Auth;
using Lantern.Api.Data;

namespace Lantern.Api.Endpoints;

public sealed record NewPromotion(string? Name, DateOnly StartsOn);

public static class HqEndpoints
{
    public static void MapHqEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard", (ClaimsPrincipal user) =>
                Results.Ok(new { greeting = $"Hello, {user.GetDisplayName()}", outlets = DemoStore.OutletIds }))
            .RequireAuthorization(Policies.HqRead);

        app.MapGet("/promotions", (DemoStore store) => Results.Ok(store.Promotions))
            .RequireAuthorization(Policies.PromotionsRead);

        app.MapPost("/promotions", (NewPromotion body, ClaimsPrincipal user, DemoStore store) =>
            {
                if (string.IsNullOrWhiteSpace(body.Name))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
                var promotion = store.AddPromotion(body.Name.Trim(), body.StartsOn, user.GetDisplayName());
                return Results.Created($"/promotions/{promotion.Id}", promotion);
            })
            .RequireAuthorization(Policies.Marketing);

        app.MapGet("/suppliers", (DemoStore store) => Results.Ok(store.Suppliers))
            .RequireAuthorization(Policies.ProcurementRead);

        app.MapGet("/stock", (DemoStore store) => Results.Ok(store.Stock))
            .RequireAuthorization(Policies.ProcurementRead);

        app.MapGet("/reports/sales", (DemoStore store) => Results.Ok(store.SalesReport))
            .RequireAuthorization(Policies.SalesReports);
    }
}
