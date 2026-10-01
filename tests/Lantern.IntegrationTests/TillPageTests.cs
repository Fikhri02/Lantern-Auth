using Lantern.IntegrationTests.Infrastructure;
using Lantern.Till.Services;
using Microsoft.Data.Sqlite;

namespace Lantern.IntegrationTests;

/// <summary>The prerendered till page shows the right screen for each till state.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class TillPageTests(KeycloakFixture kc) : IAsyncLifetime
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "lantern-till-" + Guid.NewGuid().ToString("N"));
    private TillFactory _till = null!;

    public Task InitializeAsync()
    {
        _till = new TillFactory(kc, _dataDir);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Unregistered_browser_sees_the_sign_in_prompt()
    {
        using var browser = new TillBrowser(_till);

        var page = await browser.GetAsync("/");

        Assert.Contains("data-testid=\"not-registered\"", page.Html);
        Assert.Contains("href=\"/register\"", page.Html);
    }

    [Fact]
    public async Task Registered_till_shows_its_account_and_the_pin_pad()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        using var browser = new TillBrowser(_till);

        var page = await browser.RegisterAsync(account);

        Assert.Contains("data-testid=\"pin-pad\"", page.Html);
        Assert.Contains(account, page.Html);
        Assert.DoesNotContain("data-testid=\"sale\"", page.Html);
    }

    [Fact]
    public async Task Signed_in_cashier_sees_the_sale_screen_with_their_name()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        using var browser = new TillBrowser(_till);
        await browser.RegisterAsync(account);
        await _till.Session.SignInCashierAsync(browser.DeviceId!, new PinAttempt("c-1001", "1111", null));

        var page = await browser.GetAsync("/");

        Assert.Contains("data-testid=\"sale\"", page.Html);
        Assert.Contains("Siti Aminah (c-1001)", page.Html);
        Assert.Contains("data-testid=\"sign-out-till\"", page.Html);
    }

    public async Task DisposeAsync()
    {
        await _till.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
