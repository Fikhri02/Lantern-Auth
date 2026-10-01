using Lantern.Api.Auth;
using Lantern.Api.Data;

namespace Lantern.Api.Endpoints;

public static class OutletEndpoints
{
    public static void MapOutletEndpoints(this IEndpointRouteBuilder app)
    {
        var outlet = app.MapGroup("/outlets/{outletId}");

        outlet.MapGet("/stock", (string outletId, DemoStore store) =>
                store.Stock.TryGetValue(outletId, out var lines) ? Results.Ok(lines) : OutletNotFound())
            .RequireAuthorization(Policies.OutletManage);

        outlet.MapGet("/roster", (string outletId, DemoStore store) =>
                store.Roster.TryGetValue(outletId, out var roster) ? Results.Ok(roster) : OutletNotFound())
            .RequireAuthorization(Policies.OutletManage);
    }

    private static IResult OutletNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Outlet not found.");
}
