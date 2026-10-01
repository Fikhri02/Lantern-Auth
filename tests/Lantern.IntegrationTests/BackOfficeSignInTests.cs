using Lantern.IntegrationTests.Infrastructure;
using System.Text.RegularExpressions;

namespace Lantern.IntegrationTests;

/// <summary>Spec §5.1: server-side sign-in; §3 decision 3: sid-guarded sign-out.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class BackOfficeSignInTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private AppBrowser _browser = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc);
        _browser = new AppBrowser((AppBrowser.BackOfficeOrigin, _bo.Handler()));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Anonymous_visitor_is_sent_to_keycloak()
    {
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");
        Assert.True(AppBrowser.IsKeycloakLogin(page));
    }

    [Fact]
    public async Task Hq_staff_signs_in_and_sees_their_name_and_roles()
    {
        var page = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");

        Assert.Equal(AppBrowser.BackOfficeOrigin + "/", page.Url.ToString());
        Assert.Contains("Chloe Wong", page.Html);
        Assert.Contains("data-testid=\"role-hq-staff\"", page.Html);
        Assert.Contains("data-testid=\"sign-out\"", page.Html);
    }

    [Fact]
    public async Task Tokens_never_reach_the_browser()
    {
        var page = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");
        Assert.DoesNotContain("eyJ", page.Html); // no JWT anywhere in the page
    }

    [Fact]
    public async Task Sign_out_link_ends_the_keycloak_session()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        var home = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", username);
        var signOut = Regex.Match(home.Html, "href=\"(/bff/logout\\?sid=[^\"]+)\"").Groups[1].Value;

        await _browser.GetAsync(AppBrowser.BackOfficeOrigin + System.Net.WebUtility.HtmlDecode(signOut));

        Assert.Equal(0, await kc.UserSessionCountAsync(id));
        Assert.True(AppBrowser.IsKeycloakLogin(await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/")));
    }

    [Theory]
    [InlineData("/bff/logout")]
    [InlineData("/bff/logout?sid=someone-else")]
    public async Task Sign_out_without_your_own_sid_is_refused(string path)
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", username);

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + path);

        Assert.Equal(400, page.Status);
        Assert.Equal(1, await kc.UserSessionCountAsync(id));
    }

    [Fact]
    public async Task Stale_sign_in_callback_shows_a_plain_message_not_an_error_page()
    {
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/signin-oidc?code=stale&state=forged");

        Assert.Equal(200, page.Status);
        Assert.Contains("data-testid=\"signin-problem\"", page.Html);
    }

    public async Task DisposeAsync()
    {
        _browser.Dispose();
        await _bo.DisposeAsync();
    }
}
