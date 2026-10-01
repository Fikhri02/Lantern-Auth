using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Lantern.Auth;

public static class LanternBffSetup
{
    public const string ApiClient = "lantern-api";
    public const string KeycloakClient = "keycloak";

    /// <summary>Spec §5.1: cookie + OIDC (code + PKCE), tokens kept server-side in <see cref="ServerSessionStore"/>.</summary>
    public static IServiceCollection AddLanternBff(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<LanternBffOptions>(config.GetSection(LanternBffOptions.Section));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServerSessionStore>();
        services.AddSingleton<LogoutTokenValidator>();
        services.AddHttpClient(ApiClient, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(KeycloakClient, c => c.Timeout = TimeSpan.FromSeconds(10));

        services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect();

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<LanternBffOptions>, ServerSessionStore>((o, bff, sessions) =>
            {
                o.Cookie.Name = bff.Value.CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.SessionStore = sessions;
                o.AccessDeniedPath = "/access-denied";
            });

        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<LanternBffOptions>>((o, bffOptions) =>
            {
                var bff = bffOptions.Value;
                o.MetadataAddress = $"{bff.RealmUrl}/.well-known/openid-configuration";
                o.RequireHttpsMetadata = false;
                o.ClientId = bff.ClientId;
                o.ClientSecret = bff.ClientSecret;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.ResponseMode = OpenIdConnectResponseMode.Query;
                o.UsePkce = true;
                o.Scope.Clear();
                foreach (var scope in new[] { "openid", "profile", "email" }) o.Scope.Add(scope);
                o.SaveTokens = true;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.TokenValidationParameters.ValidIssuer = bff.Issuer;
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.TokenValidationParameters.RoleClaimType = "roles";
                o.CallbackPath = "/signin-oidc";
                o.SignedOutCallbackPath = "/signout-callback-oidc";
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.NonceCookie.SameSite = SameSiteMode.Lax;
                o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, LanternAuthStateProvider>();
        services.AddAuthorization();
        return services;
    }
}
