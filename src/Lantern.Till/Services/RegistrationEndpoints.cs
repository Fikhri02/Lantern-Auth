using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Lantern.Till.Services;

/// <summary>Spec §6.1 steps 1–5: one-time outlet sign-in, encrypted registration, device cookie, end online session.</summary>
public static class RegistrationEndpoints
{
    public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/register", async (HttpContext http, TillRegistrationStore store, CancellationToken ct) =>
        {
            var deviceId = DeviceCookie.Read(http.Request);
            if (deviceId is not null && await store.FindAsync(deviceId, ct) is not null) return Results.Redirect("/");
            return Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [TillSetup.KeycloakScheme]);
        });

        app.MapGet("/signed-out", () => Results.Redirect("/"));
    }

    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var http = context.HttpContext;
        var tokens = context.TokenEndpointResponse;
        var account = context.Principal?.FindFirstValue("preferred_username");
        var outletId = context.Principal?.FindFirstValue("outlet_id");
        if (tokens is null || string.IsNullOrEmpty(tokens.RefreshToken) || string.IsNullOrEmpty(tokens.IdToken) ||
            account is null || outletId is null)
        {
            context.Fail("The outlet sign-in did not return an offline login for an outlet account.");
            return;
        }

        var store = http.RequestServices.GetRequiredService<TillRegistrationStore>();
        var time = http.RequestServices.GetRequiredService<TimeProvider>();
        var deviceId = DeviceCookie.NewId();
        await store.SaveAsync(new TillRegistration(deviceId, account, outletId, tokens.RefreshToken, time.GetUtcNow()), http.RequestAborted);
        DeviceCookie.Write(http.Response, deviceId);

        // End the online session so the till's browser keeps no Keycloak sign-in (spec §6.1 step 5).
        var configuration = await context.Options.ConfigurationManager!.GetConfigurationAsync(http.RequestAborted);
        var signedOut = $"{http.Request.Scheme}://{http.Request.Host}/signed-out";
        context.Response.Redirect($"{configuration.EndSessionEndpoint}?id_token_hint={Uri.EscapeDataString(tokens.IdToken)}" +
                                  $"&post_logout_redirect_uri={Uri.EscapeDataString(signedOut)}");
        context.HandleResponse();
    }
}
