using System.Net;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Scenario H (spec §5.7): each app's Keycloak pages carry its own theme.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class LoginThemeTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private BffAppFactory<Lantern.OutletAdmin.Program> _oa = null!;
    private AppBrowser _browser = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc);
        _oa = BffApps.OutletAdmin(kc);
        _browser = new AppBrowser((AppBrowser.BackOfficeOrigin, _bo.Handler()), (AppBrowser.OutletAdminOrigin, _oa.Handler()));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Back_office_login_uses_the_hq_theme()
    {
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");
        Assert.Contains("class=\"login-pf lantern-hq\"", page.Html);
        Assert.Contains("Sign in to Lantern Back Office", WebUtility.HtmlDecode(page.Html));
    }

    [Fact]
    public async Task Outlet_admin_login_uses_the_outlet_theme()
    {
        var page = await _browser.GetAsync(AppBrowser.OutletAdminOrigin + "/");
        Assert.Contains("class=\"login-pf lantern-outlet\"", page.Html);
        Assert.Contains("Sign in to Lantern Outlet Admin", WebUtility.HtmlDecode(page.Html));
    }

    [Fact]
    public async Task Till_login_uses_the_kiosk_theme()
    {
        var page = await _browser.GetAsync(
            $"{kc.Issuer}/protocol/openid-connect/auth?client_id={OidcBrowser.TillClientId}&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(OidcBrowser.TillRedirectUri)}&scope=openid" +
            "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"); // till requires PKCE
        Assert.Contains("class=\"login-pf lantern-till\"", page.Html);
        Assert.Contains("Sign in this till", WebUtility.HtmlDecode(page.Html));
    }

    [Fact]
    public async Task Deny_page_is_themed_too()
    {
        var page = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "mgr.bangsar");
        Assert.Contains("class=\"login-pf lantern-hq\"", page.Html);
    }

    public async Task DisposeAsync()
    {
        _browser.Dispose();
        await _bo.DisposeAsync();
        await _oa.DisposeAsync();
    }
}
