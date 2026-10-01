using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class TillAccountTests(KeycloakFixture kc)
{
    private async Task<JsonElement> TillsAsync(string reader, string outlet) =>
        await (await kc.Api.ClientAsAsync(reader)).GetFromJsonAsync<JsonElement>($"/outlets/{outlet}/tills");

    private static JsonElement Find(JsonElement tills, string username) =>
        tills.EnumerateArray().Single(t => t.GetProperty("username").GetString() == username);

    [Fact]
    public async Task Manager_sees_own_outlets_tills_and_which_are_signed_in()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");

        var tills = await TillsAsync("mgr.bangsar", "BGS");

        var signedIn = Find(tills, till.Account).GetProperty("session");
        Assert.True(signedIn.GetProperty("startedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.False(string.IsNullOrEmpty(signedIn.GetProperty("ipAddress").GetString()));
        Assert.Equal(JsonValueKind.Null, Find(tills, "outlet-bangsar-2").GetProperty("session").ValueKind);
    }

    [Fact]
    public async Task Other_outlets_manager_cannot_see_the_tills()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.klcc")).GetAsync("/outlets/BGS/tills");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Release_ends_the_till_login_and_frees_the_account()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");

        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).DeleteAsync($"/outlets/BGS/tills/{till.AccountId}/session");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await kc.RefreshTillTokenAsync(till.OfflineRefreshToken)).Status);
        await kc.OutletLoginAsync(till.Account); // would throw if the account were still taken
    }

    [Fact]
    public async Task Releasing_a_till_that_is_not_signed_in_is_404()
    {
        var (id, _) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).DeleteAsync($"/outlets/BGS/tills/{id}/session");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Till is not signed in.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Releasing_another_outlets_till_through_your_outlet_is_404()
    {
        var klccTill = await kc.GetUserIdAsync("outlet-klcc-1");
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).DeleteAsync($"/outlets/BGS/tills/{klccTill}/session");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Till not found in this outlet.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Hq_admin_adds_tills_with_increasing_numbers_and_they_work()
    {
        var admin = await kc.Api.ClientAsAsync("aisha.admin");

        var first = await (await admin.PostAsJsonAsync("/outlets/PJY/tills", new { password = "Lantern!2026" })).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await admin.PostAsJsonAsync("/outlets/PJY/tills", new { password = "Lantern!2026" })).Content.ReadFromJsonAsync<JsonElement>();

        var firstName = first.GetProperty("username").GetString()!;
        var secondName = second.GetProperty("username").GetString()!;
        Assert.StartsWith("outlet-pj-", firstName);
        Assert.Equal(int.Parse(firstName["outlet-pj-".Length..]) + 1, int.Parse(secondName["outlet-pj-".Length..]));

        var tokens = await kc.OutletLoginAsync(secondName);
        var claims = Jwt.Payload((string)(await kc.RefreshTillTokenAsync(tokens.RefreshToken)).Body["access_token"]!);
        Assert.Equal("PJY", (string?)claims["outlet_id"]);
        Assert.Contains("outlet-device", claims["roles"]!.AsArray().Select(r => (string?)r));
    }

    [Fact]
    public async Task A_half_created_account_holding_the_next_till_name_is_skipped()
    {
        var admin = await kc.Api.ClientAsAsync("aisha.admin");
        var first = await (await admin.PostAsJsonAsync("/outlets/KLC/tills", new { password = "Lantern!2026" })).Content.ReadFromJsonAsync<JsonElement>();
        var n = int.Parse(first.GetProperty("username").GetString()!["outlet-klcc-".Length..]);
        await kc.CreateBareUserAsync($"outlet-klcc-{n + 1}");

        var response = await admin.PostAsJsonAsync("/outlets/KLC/tills", new { password = "Lantern!2026" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"outlet-klcc-{n + 2}", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("username").GetString());
    }

    [Fact]
    public async Task Short_till_password_is_400()
    {
        var response = await (await kc.Api.ClientAsAsync("aisha.admin")).PostAsJsonAsync("/outlets/PJY/tills", new { password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_add_tills()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.pj")).PostAsJsonAsync("/outlets/PJY/tills", new { password = "Lantern!2026" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deactivating_a_till_account_stops_its_till()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");

        var response = await (await kc.Api.ClientAsAsync("aisha.admin")).PostAsync($"/staff/{till.AccountId}/deactivate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await kc.RefreshTillTokenAsync(till.OfflineRefreshToken)).Status);
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, "c-1001", "1111");
        Assert.Equal("outlet_session_invalid", KeycloakFixture.ErrorCode(body));
    }
}
