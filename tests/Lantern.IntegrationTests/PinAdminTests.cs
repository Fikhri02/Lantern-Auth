using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class PinAdminTests(KeycloakFixture kc)
{
    private async Task<HttpResponseMessage> PutPinAsync(string userId, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{kc.Issuer}/lantern-pin/users/{userId}")
        {
            Content = JsonContent.Create(body)
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await kc.Http.SendAsync(request);
    }

    private async Task<JsonElement[]> PinCredentialsOf(string userId)
    {
        using var admin = await kc.AdminClientAsync();
        var creds = await admin.GetFromJsonAsync<JsonElement>($"users/{userId}/credentials");
        return creds.EnumerateArray().Where(c => c.GetProperty("type").GetString() == "lantern-pin").ToArray();
    }

    [Fact]
    public async Task Setting_a_temporary_pin_stores_one_lantern_pin_credential()
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar");

        var response = await PutPinAsync(id, new { pin = "864213", temporary = true }, await kc.ServiceTokenAsync());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var pin = Assert.Single(await PinCredentialsOf(id));
        Assert.Equal("PIN (temporary)", pin.GetProperty("userLabel").GetString());
    }

    [Fact]
    public async Task Replacing_a_pin_keeps_a_single_credential()
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "864213", temporary: true);

        await PutPinAsync(id, new { pin = "2580", temporary = false }, await kc.ServiceTokenAsync());

        var pin = Assert.Single(await PinCredentialsOf(id));
        Assert.Equal("PIN", pin.GetProperty("userLabel").GetString());
    }

    [Fact]
    public async Task Seeded_cashiers_have_pins_and_c3001s_is_temporary()
    {
        var labels = new Dictionary<string, string?>();
        foreach (var code in new[] { "c-1001", "c-3001" })
            labels[code] = Assert.Single(await PinCredentialsOf(await kc.GetUserIdAsync(code))).GetProperty("userLabel").GetString();

        Assert.Equal("PIN", labels["c-1001"]);
        Assert.Equal("PIN (temporary)", labels["c-3001"]);
    }

    [Fact]
    public async Task Non_cashier_is_400()
    {
        var response = await PutPinAsync(await kc.GetUserIdAsync("eric.procure"), new { pin = "2580" }, await kc.ServiceTokenAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("not_a_cashier", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("123")]
    [InlineData("")]
    public async Task Malformed_pin_is_400(string pin)
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar");
        var response = await PutPinAsync(id, new { pin }, await kc.ServiceTokenAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_without_manage_users_is_403()
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar");
        var response = await PutPinAsync(id, new { pin = "2580" }, await kc.GetUserTokenAsync("chloe.staff"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_token_is_401()
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar");
        var response = await PutPinAsync(id, new { pin = "2580" }, token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_user_is_404_with_reason()
    {
        var response = await PutPinAsync(Guid.NewGuid().ToString(), new { pin = "2580" }, await kc.ServiceTokenAsync());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("user_not_found", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }
}
