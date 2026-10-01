using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class OutletAdminPageTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.OutletAdmin.Program> _oa = null!;
    private AppBrowser _browser = null!;

    public Task InitializeAsync()
    {
        _oa = BffApps.OutletAdmin(kc);
        _browser = new AppBrowser((AppBrowser.OutletAdminOrigin, _oa.Handler()));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Manager_sees_their_outlets_stock_and_roster()
    {
        var page = await _browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");

        Assert.Contains("data-testid=\"outlet\">BGS", page.Html);
        Assert.Contains("data-testid=\"stock-SKU-100\"", page.Html);
        Assert.Contains("42", page.Html);
        Assert.Contains("Siti Aminah", page.Html);
    }

    [Fact]
    public async Task Manager_cannot_switch_to_another_outlet()
    {
        await _browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");

        var page = await _browser.GetAsync(AppBrowser.OutletAdminOrigin + "/?outlet=KLC");

        Assert.Contains("data-testid=\"outlet\">BGS", page.Html);
        Assert.DoesNotContain("Mei Ling Chan", page.Html);
    }

    [Fact]
    public async Task Hq_admin_can_pick_any_outlet()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        await _browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", admin, totp: new Totp());

        var page = await _browser.GetAsync(AppBrowser.OutletAdminOrigin + "/?outlet=KLC");

        Assert.Contains("data-testid=\"outlet\">KLC", page.Html);
        Assert.Contains("Mei Ling Chan", page.Html);
    }

    [Fact]
    public async Task Tills_page_shows_which_till_accounts_are_signed_in()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        await _browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");

        var page = await _browser.GetAsync(AppBrowser.OutletAdminOrigin + "/tills");

        Assert.Contains($"data-testid=\"till-{till.Account}\"", page.Html);
        Assert.Contains($"data-testid=\"release-{till.Account}\"", page.Html);
        Assert.Contains("data-testid=\"till-outlet-bangsar-2\"", page.Html);
        Assert.DoesNotContain("data-testid=\"release-outlet-bangsar-2\"", page.Html);
    }

    public async Task DisposeAsync()
    {
        _browser.Dispose();
        await _oa.DisposeAsync();
    }
}
