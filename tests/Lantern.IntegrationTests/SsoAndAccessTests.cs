using System.Net;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Scenarios A (SSO), G (MFA for hq-admin) and the deny-unless-role steps (spec §4.5, §5.6).</summary>
[Collection(KeycloakCollection.Name)]
public sealed class SsoAndAccessTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private BffAppFactory<Lantern.OutletAdmin.Program> _oa = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc);
        _oa = BffApps.OutletAdmin(kc);
        return Task.CompletedTask;
    }

    private AppBrowser NewBrowser() => new(
        (AppBrowser.BackOfficeOrigin, _bo.Handler()),
        (AppBrowser.OutletAdminOrigin, _oa.Handler()));

    [Fact]
    public async Task Hq_admin_enrols_mfa_once_then_opens_outlet_admin_without_signing_in_again()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        using var browser = NewBrowser();
        var totp = new Totp();

        var backOffice = await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: totp);
        var outletAdmin = await browser.GetAsync(AppBrowser.OutletAdminOrigin + "/");

        Assert.NotNull(totp.Secret); // Keycloak made the admin set up an authenticator app
        Assert.Equal(AppBrowser.BackOfficeOrigin + "/", backOffice.Url.ToString());
        Assert.Equal(AppBrowser.OutletAdminOrigin + "/", outletAdmin.Url.ToString());
        Assert.Contains("data-testid=\"role-hq-admin\"", outletAdmin.Html);
    }

    [Fact]
    public async Task Hq_admin_is_asked_for_a_fresh_code_on_the_next_sign_in()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        var totp = new Totp();
        using (var first = NewBrowser()) await first.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: totp);
        Assert.NotNull(totp.Secret); // the first sign-in enrolled an authenticator app

        using var second = NewBrowser();
        var page = await second.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: totp);

        Assert.Equal(AppBrowser.BackOfficeOrigin + "/", page.Url.ToString());
    }

    [Fact]
    public async Task Staff_without_admin_role_are_not_asked_for_mfa()
    {
        using var browser = NewBrowser();
        var page = await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");
        Assert.Equal(AppBrowser.BackOfficeOrigin + "/", page.Url.ToString()); // would throw if an OTP page appeared
    }

    [Fact]
    public async Task Outlet_manager_is_refused_by_back_office_at_keycloak()
    {
        using var browser = NewBrowser();
        var page = await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "mgr.bangsar");

        Assert.Contains("Your account doesn't have access to Back Office.", WebUtility.HtmlDecode(page.Html));
        Assert.NotEqual(AppBrowser.BackOfficeOrigin, page.Url.GetLeftPart(UriPartial.Authority));
    }

    [Fact]
    public async Task Outlet_manager_signs_in_to_outlet_admin()
    {
        using var browser = NewBrowser();
        var page = await browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");
        Assert.Contains("data-testid=\"role-outlet-manager\"", page.Html);
    }

    [Fact]
    public async Task Hq_staff_are_refused_by_outlet_admin()
    {
        using var browser = NewBrowser();
        var page = await browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "chloe.staff");
        Assert.Contains("Your account doesn't have access to Outlet Admin.", WebUtility.HtmlDecode(page.Html));
    }

    [Fact]
    public async Task Single_sign_on_from_outlet_admin_does_not_skip_back_offices_role_check()
    {
        using var browser = NewBrowser();
        await browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");

        var page = await browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");

        Assert.Contains("Your account doesn't have access to Back Office.", WebUtility.HtmlDecode(page.Html));
    }

    public async Task DisposeAsync()
    {
        await _bo.DisposeAsync();
        await _oa.DisposeAsync();
    }
}
