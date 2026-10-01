using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Lantern.Till.Services;

public static class TillSetup
{
    public const string SignInScheme = "till-signin";
    public const string KeycloakScheme = "keycloak";

    public static IServiceCollection AddLanternTill(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TillOptions>(config.GetSection(TillOptions.Section));

        services.AddDataProtection().SetApplicationName("lantern-till");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<TillOptions>, ILoggerFactory>((keys, till, logs) =>
                keys.XmlRepository = new FileSystemXmlRepository(
                    new DirectoryInfo(Path.Combine(till.Value.DataDirectory, "keys")), logs));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TillRegistrationStore>();
        services.AddSingleton<KeycloakTillClient>();
        services.AddSingleton<LanternApiClient>();
        services.AddSingleton<TillSessionService>();
        services.AddHttpClient("keycloak", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient("lantern-api", c => c.Timeout = TimeSpan.FromSeconds(10));

        // The OIDC handler is used only for the one-time outlet sign-in; RegistrationEndpoints.OnTokenValidated
        // stores the registration and ends the response, so the till never holds a cookie session.
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = SignInScheme;
                o.DefaultChallengeScheme = KeycloakScheme;
            })
            .AddCookie(SignInScheme, o => o.Cookie.Name = "lantern.till.signin")
            .AddOpenIdConnect(KeycloakScheme, _ => { });

        services.AddOptions<OpenIdConnectOptions>(KeycloakScheme)
            .Configure<IOptions<TillOptions>>((o, tillOptions) =>
            {
                var till = tillOptions.Value;
                o.MetadataAddress = $"{till.RealmUrl}/.well-known/openid-configuration";
                o.RequireHttpsMetadata = false;
                o.ClientId = till.ClientId;
                o.ClientSecret = till.ClientSecret;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.ResponseMode = OpenIdConnectResponseMode.Query;
                o.UsePkce = true;
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("offline_access");
                o.SaveTokens = false;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.TokenValidationParameters.ValidIssuer = till.Issuer;
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.CallbackPath = "/signin-oidc";
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.NonceCookie.SameSite = SameSiteMode.Lax;
                o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnTokenValidated = RegistrationEndpoints.OnTokenValidatedAsync;
                o.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/?signin=failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        return services;
    }
}
