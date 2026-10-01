using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class AuthenticationTests(KeycloakFixture kc)
{
    private sealed record Me(string Id, string Name, string[] Roles, string? OutletId);

    [Fact]
    public async Task Me_without_token_is_401()
    {
        var response = await kc.Api.CreateClient().GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Outlet_manager_token_carries_role_and_outlet_id()
    {
        var client = await kc.Api.ClientAsAsync("mgr.bangsar");

        var me = await client.GetFromJsonAsync<Me>("/me");

        Assert.Equal("mgr.bangsar", me!.Name);
        Assert.Contains("outlet-manager", me.Roles);
        Assert.Equal("BGS", me.OutletId);
        Assert.False(string.IsNullOrEmpty(me.Id));
    }

    [Fact]
    public async Task Hq_user_gets_roles_from_department_group_and_no_outlet()
    {
        var client = await kc.Api.ClientAsAsync("eric.procure");

        var me = await client.GetFromJsonAsync<Me>("/me");

        Assert.Equal(new[] { "hq-staff", "procurement" }, me!.Roles);
        Assert.Null(me.OutletId);
    }

    [Fact]
    public async Task Token_from_another_realm_is_401()
    {
        using var masterToken = await kc.Http.PostAsync($"{kc.BaseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = "admin", ["password"] = "admin"
            }));
        var token = (string)JsonNode.Parse(await masterToken.Content.ReadAsStringAsync())!["access_token"]!;

        var response = await kc.Api.ClientWithToken(token).GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Garbage_token_is_401()
    {
        var response = await kc.Api.ClientWithToken("not-a-jwt").GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
