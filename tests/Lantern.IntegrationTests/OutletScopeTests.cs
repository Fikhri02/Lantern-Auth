using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class OutletScopeTests(KeycloakFixture kc)
{
    [Theory]
    [InlineData("mgr.bangsar", "/outlets/BGS/stock", HttpStatusCode.OK)]
    [InlineData("mgr.bangsar", "/outlets/bgs/stock", HttpStatusCode.OK)]
    [InlineData("mgr.bangsar", "/outlets/KLC/stock", HttpStatusCode.Forbidden)]
    [InlineData("mgr.klcc", "/outlets/KLC/roster", HttpStatusCode.OK)]
    [InlineData("mgr.klcc", "/outlets/BGS/roster", HttpStatusCode.Forbidden)]
    [InlineData("aisha.admin", "/outlets/PJY/stock", HttpStatusCode.OK)]
    [InlineData("outlet-bangsar-1", "/outlets/BGS/roster", HttpStatusCode.Forbidden)]
    [InlineData("eric.procure", "/outlets/BGS/stock", HttpStatusCode.Forbidden)]
    [InlineData("mgr.bangsar", "/outlets/XXX/stock", HttpStatusCode.Forbidden)]
    public async Task Outlet_routes_match_token_outlet(string user, string path, HttpStatusCode expected)
    {
        var response = await (await kc.Api.ClientAsAsync(user)).GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Lowercase_outlet_returns_that_outlets_data()
    {
        var client = await kc.Api.ClientAsAsync("mgr.bangsar");
        var stock = await client.GetFromJsonAsync<JsonElement>("/outlets/bgs/stock");
        Assert.Equal(42, stock.EnumerateArray().First().GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Unknown_outlet_is_404_for_hq_admin()
    {
        var response = await (await kc.Api.ClientAsAsync("aisha.admin")).GetAsync("/outlets/XXX/stock");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Outlet not found.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Forbidden_outlet_names_the_policy()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).GetAsync("/outlets/KLC/stock");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OutletManage", problem.GetProperty("policy").GetString());
    }
}
