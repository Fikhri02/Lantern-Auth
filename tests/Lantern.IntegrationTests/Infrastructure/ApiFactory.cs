using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests.Infrastructure;

public sealed class ApiFactory(KeycloakFixture kc, int introspectionCacheSeconds,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:BaseUrl"] = kc.BaseUrl,
            ["Keycloak:Issuer"] = kc.Issuer,
            ["Keycloak:IntrospectionCacheSeconds"] = introspectionCacheSeconds.ToString()
        }));
        if (configureServices is not null) builder.ConfigureTestServices(configureServices);
    }

    public async Task<HttpClient> ClientAsAsync(string username) =>
        ClientWithToken(await kc.GetUserTokenAsync(username));

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
