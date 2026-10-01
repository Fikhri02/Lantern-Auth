using System.Net;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.1 E: one till account can be signed in on only one till at a time.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class TillAccountLimitTests(KeycloakFixture kc)
{
    private const string InUse = "already signed in on another till";

    private async Task<LoginOutcome> SecondTillLoginAsync(string account)
    {
        using var browser = new OidcBrowser(kc.Issuer);
        return await browser.LoginAsync(account, KeycloakFixture.DemoPassword);
    }

    [Fact]
    public async Task Second_till_cannot_sign_in_with_the_same_account()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await kc.OutletLoginAsync(account);

        var second = await SecondTillLoginAsync(account);

        Assert.False(second.Succeeded);
        Assert.Contains(InUse, second.Html);
    }

    [Fact]
    public async Task Refused_even_while_the_first_tills_online_session_is_open()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await kc.OutletLoginAsync(account, endOnlineSession: false);

        var second = await SecondTillLoginAsync(account);

        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task Same_browser_cannot_skip_the_password_and_the_limit()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        using var browser = new OidcBrowser(kc.Issuer);
        Assert.True((await browser.LoginAsync(account, KeycloakFixture.DemoPassword)).Succeeded);

        var again = await browser.LoginAsync(account, KeycloakFixture.DemoPassword);

        Assert.False(again.Succeeded);
        Assert.Contains(InUse, again.Html);
    }

    [Fact]
    public async Task Releasing_the_account_lets_another_till_sign_in()
    {
        var (id, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await kc.OutletLoginAsync(account);
        Assert.False((await SecondTillLoginAsync(account)).Succeeded);

        await kc.RevokeTillLoginAsync(id);

        Assert.True((await SecondTillLoginAsync(account)).Succeeded);
    }

    [Fact]
    public async Task Non_outlet_account_cannot_open_a_till()
    {
        var outcome = await SecondTillLoginAsync("eric.procure");

        Assert.False(outcome.Succeeded);
        Assert.Contains("can't open a till", WebUtility.HtmlDecode(outcome.Html));
    }
}
