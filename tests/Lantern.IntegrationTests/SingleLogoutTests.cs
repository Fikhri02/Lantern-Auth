using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Scenario B (spec §5.2) and deactivation reaching open sessions (spec §5.4).</summary>
[Collection(KeycloakCollection.Name)]
public sealed class SingleLogoutTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private BffAppFactory<Lantern.OutletAdmin.Program> _oa = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc);
        _oa = BffApps.OutletAdmin(kc);
        return Task.CompletedTask;
    }

    private AppBrowser NewBrowser() => new((AppBrowser.BackOfficeOrigin, _bo.Handler()), (AppBrowser.OutletAdminOrigin, _oa.Handler()));

    [Fact]
    public async Task Signing_out_of_back_office_signs_you_out_of_outlet_admin()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        using var browser = NewBrowser();
        var home = await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: new Totp());
        await browser.GetAsync(AppBrowser.OutletAdminOrigin + "/");
        var signOut = System.Net.WebUtility.HtmlDecode(Regex.Match(home.Html, "href=\"(/bff/logout\\?sid=[^\"]+)\"").Groups[1].Value);

        await browser.GetAsync(AppBrowser.BackOfficeOrigin + signOut);

        Assert.True(AppBrowser.IsKeycloakLogin(await browser.GetAsync(AppBrowser.OutletAdminOrigin + "/")));
        Assert.True(AppBrowser.IsKeycloakLogin(await browser.GetAsync(AppBrowser.BackOfficeOrigin + "/")));
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_open_back_office_session()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        using var browser = NewBrowser();
        await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", username);

        var deactivate = await (await kc.Api.ClientAsAsync("aisha.admin")).PostAsync($"/staff/{id}/deactivate", null);

        Assert.True(deactivate.IsSuccessStatusCode);
        Assert.True(AppBrowser.IsKeycloakLogin(await browser.GetAsync(AppBrowser.BackOfficeOrigin + "/")));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("")]
    public async Task Logout_token_that_is_not_from_keycloak_is_refused(string token)
    {
        using var client = _bo.CreateClient();
        var response = await client.PostAsync("/bff/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = token }));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_logout_token", await response.Content.ReadAsStringAsync()); // the endpoint's own refusal
    }

    [Fact]
    public async Task Logout_token_meant_for_another_client_is_refused()
    {
        // A token signed by Keycloak but issued to another audience: the test-runner's access token.
        var foreign = await kc.GetUserTokenAsync("chloe.staff");
        using var client = _bo.CreateClient();

        var response = await client.PostAsync("/bff/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = foreign }));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_logout_token", await response.Content.ReadAsStringAsync()); // the endpoint's own refusal
    }

    [Fact]
    public async Task Real_logout_token_for_outlet_admin_is_refused_by_back_office()
    {
        var (id, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        using var browser = new AppBrowser((AppBrowser.OutletAdminOrigin, _oa.Handler()));
        await browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", admin, totp: new Totp());
        var capture = new CapturingHandler();
        kc.Relay.Route("outlet-admin", capture);

        await kc.LogoutUserAsync(id); // Keycloak sends outlet-admin a signed logout token; we keep it
        var outletAdminToken = await capture.Token.Task.WaitAsync(TimeSpan.FromSeconds(15));
        using var client = _bo.CreateClient();
        var response = await client.PostAsync("/bff/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = outletAdminToken }));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_logout_token", await response.Content.ReadAsStringAsync());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public TaskCompletionSource<string> Token { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(ct));
            Token.TrySetResult(form["logout_token"]!);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    public async Task DisposeAsync()
    {
        await _bo.DisposeAsync();
        await _oa.DisposeAsync();
    }
}
