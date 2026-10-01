using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class CashierManagementTests(KeycloakFixture kc)
{
    private Task<HttpClient> Admin() => kc.Api.ClientAsAsync("aisha.admin");

    private async Task<JsonElement> CreateCashierAsync(string outletId, string first = "Amir", string last = "Hakim")
    {
        var response = await (await Admin()).PostAsJsonAsync("/cashiers", new { outletId, firstName = first, lastName = last });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task New_cashier_signs_in_with_the_temporary_pin_then_chooses_their_own()
    {
        var created = await CreateCashierAsync("PJY");
        var code = created.GetProperty("code").GetString()!;
        var temporaryPin = created.GetProperty("temporaryPin").GetString()!;
        var till = await kc.OpenTillAsync("/Outlets/PJ");

        Assert.StartsWith("c-3", code);
        Assert.Equal("pin_change_required", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, code, temporaryPin)).Body));
        var (status, body) = await kc.CashierPinAsync(till.AccessToken, code, temporaryPin, "2580");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(code, (string?)Jwt.Payload((string)body["access_token"]!)["preferred_username"]);
    }

    [Fact]
    public async Task Codes_are_unique_within_an_outlet()
    {
        var first = (await CreateCashierAsync("KLC")).GetProperty("code").GetString()!;
        var second = (await CreateCashierAsync("KLC")).GetProperty("code").GetString()!;

        Assert.StartsWith("c-2", first);
        Assert.Equal(int.Parse(first[3..]) + 1, int.Parse(second[3..]));
    }

    [Fact]
    public async Task Reset_pin_replaces_the_old_pin_and_lifts_a_lockout()
    {
        var created = await CreateCashierAsync("BGS");
        var id = created.GetProperty("id").GetString()!;
        var code = created.GetProperty("code").GetString()!;
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        await kc.CashierPinAsync(till.AccessToken, code, created.GetProperty("temporaryPin").GetString()!, "2580");
        for (var i = 0; i < 5; i++) await kc.CashierPinAsync(till.AccessToken, code, "0000");
        Assert.Equal("pin_locked", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, code, "2580")).Body));

        var reset = await (await Admin()).PostAsync($"/cashiers/{id}/reset-pin", null);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var newTemporary = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPin").GetString()!;
        Assert.Equal("pin_change_required", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, code, newTemporary)).Body));
        Assert.StartsWith("pin_invalid", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, code, "2580")).Body));
    }

    [Fact]
    public async Task Manager_cannot_create_cashiers()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).PostAsJsonAsync("/cashiers",
            new { outletId = "BGS", firstName = "X", lastName = "Y" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("XXX", "Amir", "Hakim")]
    [InlineData("BGS", "", "Hakim")]
    [InlineData("BGS", "Amir (HQ)", "Hakim")]
    public async Task Invalid_cashier_is_400(string outletId, string first, string last)
    {
        var response = await (await Admin()).PostAsJsonAsync("/cashiers", new { outletId, firstName = first, lastName = last });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reset_pin_for_a_non_cashier_is_400()
    {
        var response = await (await Admin()).PostAsync($"/cashiers/{await kc.GetUserIdAsync("eric.procure")}/reset-pin", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reset_pin_for_an_unknown_user_is_404_with_reason()
    {
        var response = await (await Admin()).PostAsync($"/cashiers/{Guid.NewGuid()}/reset-pin", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Cashier not found.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }
}
