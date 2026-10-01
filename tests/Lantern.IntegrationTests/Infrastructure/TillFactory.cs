using Lantern.Till.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>The Till app in memory, pointed at the test Keycloak and the in-memory API.</summary>
public sealed class TillFactory(KeycloakFixture kc, string dataDirectory, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Lantern.Till.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Till:KeycloakBaseUrl"] = kc.BaseUrl,
            ["Till:Issuer"] = kc.Issuer,
            ["Till:ApiBaseUrl"] = "http://lantern-api",
            ["Till:DataDirectory"] = dataDirectory
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient("lantern-api").ConfigurePrimaryHttpMessageHandler(() => kc.Api.Server.CreateHandler());
            configureServices?.Invoke(services);
        });
    }

    public TillRegistrationStore Store => Services.GetRequiredService<TillRegistrationStore>();
    public TillSessionService Session => Services.GetRequiredService<TillSessionService>();
}
