using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lantern.Auth;

public static class BffEndpoints
{
    public static IEndpointRouteBuilder MapLanternBff(this IEndpointRouteBuilder app)
    {
        app.MapGet("/bff/login", (string? returnUrl) =>
            Results.Challenge(new AuthenticationProperties { RedirectUri = LocalOnly(returnUrl) },
                [OpenIdConnectDefaults.AuthenticationScheme]));

        // The sid parameter must match the signed-in session, so a link on another site can't sign anyone out.
        app.MapGet("/bff/logout", (HttpContext http, string? sid) =>
        {
            var mine = LanternRoles.Sid(http.User);
            if (http.User.Identity?.IsAuthenticated != true || mine is null || sid != mine)
                return Results.BadRequest();
            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        });

        return app;
    }

    private static string LocalOnly(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";
}
