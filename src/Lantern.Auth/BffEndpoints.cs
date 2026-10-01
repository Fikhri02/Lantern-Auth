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
            // Bodies on these 400s matter: the apps' status-code pages re-execute empty error responses.
            if (http.User.Identity?.IsAuthenticated != true || mine is null || sid != mine)
                return Results.BadRequest("This sign-out link isn't for your session.");
            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        });


        // Keycloak calls this for every client of a session that ended anywhere (spec §5.2).
        app.MapPost("/bff/backchannel-logout", async (HttpContext http, LogoutTokenValidator validator, ServerSessionStore sessions) =>
            {
                var form = await http.Request.ReadFormAsync(http.RequestAborted);
                var sid = await validator.ValidateAsync(form["logout_token"], http.RequestAborted);
                if (sid is null) return Results.BadRequest("invalid_logout_token");
                await sessions.RemoveBySidAsync(sid);
                return Results.Ok();
            })
            .DisableAntiforgery()
            .AllowAnonymous();

        return app;
    }

    private static string LocalOnly(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";
}
