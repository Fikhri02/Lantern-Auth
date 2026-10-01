using System.Net;
using System.Text;
using Lantern.IntegrationTests.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.1 steps 1–6, from the till's side.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class TillRegistrationTests(KeycloakFixture kc) : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "lantern-till-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Registering_stores_an_encrypted_registration_and_ends_the_online_session()
    {
        var (accountId, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await using var till = new TillFactory(kc, _dataDir);
        using var browser = new TillBrowser(till);

        var landed = await browser.RegisterAsync(account);

        Assert.Equal(TillBrowser.TillOrigin + "/", landed.Url.ToString());
        Assert.Equal(200, landed.Status);
        var registration = await till.Store.FindAsync(browser.DeviceId!);
        Assert.Equal(account, registration!.Account);
        Assert.Equal("BGS", registration.OutletId);

        SqliteConnection.ClearAllPools();
        Assert.DoesNotContain(registration.RefreshToken, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(till.Store.DatabasePath)));

        Assert.Equal(0, await kc.UserSessionCountAsync(accountId));
        Assert.Equal(1, await kc.OfflineTillSessionCountAsync(accountId));
    }

    [Fact]
    public async Task Registration_survives_restarting_the_till()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        string deviceId;
        await using (var first = new TillFactory(kc, _dataDir))
        {
            using var browser = new TillBrowser(first);
            await browser.RegisterAsync(account);
            deviceId = browser.DeviceId!;
        }

        await using var restarted = new TillFactory(kc, _dataDir);

        Assert.Equal(account, (await restarted.Store.FindAsync(deviceId))!.Account);
    }

    [Fact]
    public async Task A_second_till_with_the_same_account_is_refused_and_not_registered()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await using var till = new TillFactory(kc, _dataDir);
        using var firstTill = new TillBrowser(till);
        using var secondTill = new TillBrowser(till);
        await firstTill.RegisterAsync(account);

        var refused = await secondTill.RegisterAsync(account);

        Assert.Contains("already signed in on another till", WebUtility.HtmlDecode(refused.Html));
        Assert.Null(secondTill.DeviceId);
    }

    [Fact]
    public async Task A_registered_till_going_to_register_again_stays_home()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await using var till = new TillFactory(kc, _dataDir);
        using var browser = new TillBrowser(till);
        await browser.RegisterAsync(account);

        var page = await browser.GetAsync("/register");

        Assert.Equal(TillBrowser.TillOrigin + "/", page.Url.ToString());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
