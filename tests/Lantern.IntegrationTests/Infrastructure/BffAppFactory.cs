using Lantern.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>Back Office or Outlet Admin in memory, pointed at the test Keycloak and the in-memory API.</summary>
public sealed class BffAppFactory<TProgram>(KeycloakFixture kc, string clientId, string clientSecret, string cookieName,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lantern:KeycloakBaseUrl"] = kc.BaseUrl,
            ["Lantern:Issuer"] = kc.Issuer,
            ["Lantern:ClientId"] = clientId,
            ["Lantern:ClientSecret"] = clientSecret,
            ["Lantern:CookieName"] = cookieName,
            ["Lantern:ApiBaseUrl"] = "http://lantern-api"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(LanternBffSetup.ApiClient).ConfigurePrimaryHttpMessageHandler(() => kc.Api.Server.CreateHandler());
            configureServices?.Invoke(services);
        });
    }

    public ServerSessionStore SessionStore => Services.GetRequiredService<ServerSessionStore>();

    /// <summary>A handler into the in-memory app, and Keycloak's back-channel calls for this client routed to it.</summary>
    public HttpMessageHandler Handler()
    {
        kc.Relay.Route(clientId, Server.CreateHandler());
        return Server.CreateHandler();
    }
}

public static class BffApps
{
    public static BffAppFactory<Lantern.BackOffice.Program> BackOffice(KeycloakFixture kc, Action<IServiceCollection>? configure = null) =>
        new(kc, "backoffice", "backoffice-dev-secret", "lantern.bo", configure);

    public static BffAppFactory<Lantern.OutletAdmin.Program> OutletAdmin(KeycloakFixture kc, Action<IServiceCollection>? configure = null) =>
        new(kc, "outlet-admin", "outlet-admin-dev-secret", "lantern.oa", configure);
}
