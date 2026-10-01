using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class RolePolicyTests(KeycloakFixture kc)
{
    [Theory]
    [InlineData("chloe.staff", "/dashboard", HttpStatusCode.OK)]
    [InlineData("aisha.admin", "/dashboard", HttpStatusCode.OK)]
    [InlineData("mgr.bangsar", "/dashboard", HttpStatusCode.Forbidden)]
    [InlineData("outlet-bangsar-1", "/dashboard", HttpStatusCode.Forbidden)]
    [InlineData("chloe.staff", "/promotions", HttpStatusCode.OK)]
    [InlineData("mgr.bangsar", "/promotions", HttpStatusCode.OK)]
    [InlineData("outlet-bangsar-1", "/promotions", HttpStatusCode.Forbidden)]
    [InlineData("eric.procure", "/suppliers", HttpStatusCode.OK)]
    [InlineData("aisha.admin", "/suppliers", HttpStatusCode.OK)]
    [InlineData("dina.marketing", "/suppliers", HttpStatusCode.Forbidden)]
    [InlineData("eric.procure", "/stock", HttpStatusCode.OK)]
    [InlineData("chloe.staff", "/stock", HttpStatusCode.Forbidden)]
    [InlineData("farah.finance", "/reports/sales", HttpStatusCode.OK)]
    [InlineData("aisha.admin", "/reports/sales", HttpStatusCode.OK)]
    [InlineData("eric.procure", "/reports/sales", HttpStatusCode.Forbidden)]
    public async Task Read_endpoints_follow_roles(string user, string path, HttpStatusCode expected)
    {
        var client = await kc.Api.ClientAsAsync(user);
        var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("dina.marketing", HttpStatusCode.Created)]
    [InlineData("gary.multi", HttpStatusCode.Created)]
    [InlineData("chloe.staff", HttpStatusCode.Forbidden)]
    [InlineData("aisha.admin", HttpStatusCode.Forbidden)]
    [InlineData("mgr.bangsar", HttpStatusCode.Forbidden)]
    public async Task Only_marketing_creates_promotions(string user, HttpStatusCode expected)
    {
        var client = await kc.Api.ClientAsAsync(user);
        var response = await client.PostAsJsonAsync("/promotions", new { name = "Raya Week", startsOn = "2026-11-01" });
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Created_promotion_records_who_created_it()
    {
        var client = await kc.Api.ClientAsAsync("dina.marketing");
        var response = await client.PostAsJsonAsync("/promotions", new { name = "Deepavali Deals", startsOn = "2026-11-08" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Dina Kaur", body.GetProperty("createdBy").GetString());
    }

    [Fact]
    public async Task Forbidden_response_names_the_policy()
    {
        var client = await kc.Api.ClientAsAsync("chloe.staff");
        var response = await client.PostAsJsonAsync("/promotions", new { name = "X", startsOn = "2026-11-01" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Marketing", problem.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task Promotion_without_name_is_400()
    {
        var client = await kc.Api.ClientAsAsync("dina.marketing");
        var response = await client.PostAsJsonAsync("/promotions", new { name = " ", startsOn = "2026-11-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
