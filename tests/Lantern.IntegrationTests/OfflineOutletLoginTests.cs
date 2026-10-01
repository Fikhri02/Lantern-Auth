using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.1 and risk §12.3: the outlet login is an offline session that outlives the browser session.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class OfflineOutletLoginTests(KeycloakFixture kc)
{
    [Fact]
    public async Task Outlet_login_returns_an_offline_refresh_token()
    {
        var (_, till) = await kc.CreateTempTillAsync("/Outlets/Bangsar");

        var tokens = await kc.OutletLoginAsync(till);

        Assert.Equal("Offline", (string?)Jwt.Payload(tokens.RefreshToken)["typ"]);
    }

    [Fact]
    public async Task Seeded_outlet_account_gets_an_offline_login()
    {
        // Imported users don't get the realm's default roles, so offline_access must come from outlet-device.
        var tokens = await kc.OutletLoginAsync("outlet-pj-1");
        try
        {
            Assert.Equal("Offline", (string?)Jwt.Payload(tokens.RefreshToken)["typ"]);
        }
        finally
        {
            await kc.RevokeTillLoginAsync(await kc.GetUserIdAsync("outlet-pj-1"));
        }
    }

    [Fact]
    public async Task Offline_login_survives_ending_the_online_session()
    {
        var (_, till) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        var tokens = await kc.OutletLoginAsync(till, endOnlineSession: true);

        var (status, body) = await kc.RefreshTillTokenAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(string.IsNullOrEmpty((string?)body["access_token"]));
    }

    [Fact]
    public async Task Refreshed_access_token_names_the_offline_session()
    {
        var (id, till) = await kc.CreateTempTillAsync("/Outlets/KLCC");
        var tokens = await kc.OutletLoginAsync(till);
        var (_, body) = await kc.RefreshTillTokenAsync(tokens.RefreshToken);
        var sid = (string?)Jwt.Payload((string)body["access_token"]!)["sid"];

        using var admin = await kc.AdminClientAsync();
        var sessions = await admin.GetFromJsonAsync<JsonElement>($"users/{id}/offline-sessions/{await kc.GetTillClientUuidAsync()}");

        Assert.False(string.IsNullOrEmpty(sid));
        Assert.Contains(sid, sessions.EnumerateArray().Select(s => s.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Revoking_the_till_consent_ends_the_outlet_login()
    {
        var (id, till) = await kc.CreateTempTillAsync("/Outlets/PJ");
        var tokens = await kc.OutletLoginAsync(till);

        await kc.RevokeTillLoginAsync(id);
        var (status, body) = await kc.RefreshTillTokenAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid_grant", (string?)body["error"]);
    }
}
