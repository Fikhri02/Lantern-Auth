using System.Net.Http.Json;
using Lantern.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Lantern.IntegrationTests;

/// <summary>Back Office pages per role (scenario C in the UI), and token refresh behind them.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class BackOfficePageTests(KeycloakFixture kc) : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private AppBrowser _browser = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc, s => s.AddSingleton<TimeProvider>(_time));
        _browser = new AppBrowser((AppBrowser.BackOfficeOrigin, _bo.Handler()));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Home_greets_the_user_with_data_from_the_api()
    {
        var page = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");
        Assert.Contains("Hello, Chloe Wong", page.Html);
    }

    [Fact]
    public async Task Staff_page_lists_staff_for_an_hq_admin()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: new Totp());

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/staff");

        Assert.Contains("data-testid=\"staff-eric.procure\"", page.Html);
        Assert.Contains("data-testid=\"create-staff\"", page.Html);
    }

    [Fact]
    public async Task Staff_page_is_off_limits_to_other_hq_staff()
    {
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "dina.marketing");

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/staff");

        Assert.Contains("data-testid=\"access-denied\"", page.Html);
        Assert.DoesNotContain("staff-eric.procure", page.Html);
    }

    [Fact]
    public async Task Marketing_sees_promotions_and_the_create_form()
    {
        await (await kc.Api.ClientAsAsync("dina.marketing")).PostAsJsonAsync("/promotions", new { name = "Merdeka Mornings", startsOn = "2026-08-31" });
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "dina.marketing");

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/promotions");

        Assert.Contains("Merdeka Mornings", page.Html);
        Assert.Contains("data-testid=\"create-promotion\"", page.Html);
    }

    [Fact]
    public async Task Other_staff_see_promotions_with_a_try_anyway_button_instead_of_the_form()
    {
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/promotions");

        Assert.DoesNotContain("data-testid=\"create-promotion\"", page.Html);
        Assert.Contains("data-testid=\"try-anyway\"", page.Html);
    }

    [Fact]
    public async Task Pages_keep_working_after_the_access_token_expires()
    {
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");

        _time.Advance(TimeSpan.FromMinutes(6)); // access token (5 min) now expired; refresh token still valid
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");

        Assert.Contains("Hello, Chloe Wong", page.Html);
    }

    [Fact]
    public async Task Ended_keycloak_session_sends_the_user_to_sign_in_on_the_next_api_call()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", username);
        kc.Relay.Route("backoffice", new SwallowHandler()); // drop the back-channel call: only the token refresh can notice
        await kc.LogoutUserAsync(id);

        _time.Advance(TimeSpan.FromMinutes(6));
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");

        Assert.True(AppBrowser.IsKeycloakLogin(page));
    }

    private sealed class SwallowHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    public async Task DisposeAsync()
    {
        _browser.Dispose();
        await _bo.DisposeAsync();
    }
}
