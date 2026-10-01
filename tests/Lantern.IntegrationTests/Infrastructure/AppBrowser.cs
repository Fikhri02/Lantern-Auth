using System.Net;
using System.Text.RegularExpressions;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// A browser across several in-memory apps (by origin) and the real Keycloak. Follows redirects, keeps
/// cookies per origin, and can complete Keycloak's password, TOTP-setup and OTP pages.
/// </summary>
public sealed partial class AppBrowser : IDisposable
{
    public const string BackOfficeOrigin = "http://localhost:5200";
    public const string OutletAdminOrigin = "http://localhost:5300";

    private readonly LocalhostCookieJar _jar;
    private readonly HttpClient _http;

    public AppBrowser(params (string Origin, HttpMessageHandler Handler)[] apps)
    {
        _jar = new LocalhostCookieJar(new Router(apps.ToDictionary(a => a.Origin, a => new HttpMessageInvoker(a.Handler)),
            new HttpMessageInvoker(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })));
        _http = new HttpClient(_jar);
    }

    public Task<BrowserPage> GetAsync(string url) => FollowAsync(new HttpRequestMessage(HttpMethod.Get, url));

    public static bool IsKeycloakLogin(BrowserPage page) => FormAction(page.Html, "kc-form-login") is not null;

    public static bool AsksToSetUpTotp(BrowserPage page) => FormAction(page.Html, "kc-totp-settings-form") is not null;

    /// <summary>Fills in Keycloak's password form only, so a test can see what Keycloak asks for next.</summary>
    public Task<BrowserPage> SubmitPasswordAsync(BrowserPage loginPage, string username, string password = KeycloakFixture.DemoPassword) =>
        PostAsync(FormAction(loginPage.Html, "kc-form-login") ?? throw new InvalidOperationException("Not a Keycloak login page"),
            new() { ["username"] = username, ["password"] = password, ["credentialId"] = "" });

    /// <summary>Opens <paramref name="url"/>; if Keycloak asks, signs in (enrolling or answering TOTP when it asks).</summary>
    public async Task<BrowserPage> SignInAsync(string url, string username, string password = KeycloakFixture.DemoPassword, Totp? totp = null)
    {
        var page = await GetAsync(url);
        if (!IsKeycloakLogin(page)) return page;

        page = await PostAsync(FormAction(page.Html, "kc-form-login")!, new()
        {
            ["username"] = username, ["password"] = password, ["credentialId"] = ""
        });

        if (FormAction(page.Html, "kc-totp-settings-form") is { } setup)
        {
            totp ??= new Totp();
            totp.Secret = HiddenValue(page.Html, "totpSecret");
            page = await PostAsync(setup, new()
            {
                ["totp"] = totp.NextCode(), ["totpSecret"] = totp.Secret!, ["userLabel"] = "test phone"
            });
        }
        else if (FormAction(page.Html, "kc-otp-login-form") is { } otp)
        {
            if (totp?.Secret is null) throw new InvalidOperationException("Keycloak asked for a one-time code but the test has no TOTP secret.");
            page = await PostAsync(otp, new() { ["otp"] = totp.NextCode() });
        }
        return page;
    }

    public void Dispose() => _http.Dispose();

    private Task<BrowserPage> PostAsync(string action, Dictionary<string, string> form) =>
        FollowAsync(new HttpRequestMessage(HttpMethod.Post, action) { Content = new FormUrlEncodedContent(form) });

    private async Task<BrowserPage> FollowAsync(HttpRequestMessage request)
    {
        for (var hop = 0; hop < 25; hop++)
        {
            using var response = await _http.SendAsync(request);
            var url = request.RequestUri!;
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                request = new HttpRequestMessage(HttpMethod.Get, location.IsAbsoluteUri ? location : new Uri(url, location));
                continue;
            }
            return new BrowserPage(url, (int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        throw new InvalidOperationException("Too many redirects");
    }

    [GeneratedRegex("<form\\b[^>]*>")]
    private static partial Regex FormTag();

    [GeneratedRegex("\\baction=\"([^\"]+)\"")]
    private static partial Regex ActionAttribute();

    private static string? FormAction(string html, string formId)
    {
        var tag = FormTag().Matches(html).FirstOrDefault(m => m.Value.Contains($"id=\"{formId}\""));
        if (tag is null) return null;
        var action = ActionAttribute().Match(tag.Value);
        return action.Success ? WebUtility.HtmlDecode(action.Groups[1].Value) : null;
    }

    private static string HiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $"<input[^>]*name=\"{name}\"[^>]*value=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException($"No hidden field {name}");
    }

    private sealed class Router(Dictionary<string, HttpMessageInvoker> apps, HttpMessageInvoker network) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            apps.TryGetValue(request.RequestUri!.GetLeftPart(UriPartial.Authority), out var app)
                ? app.SendAsync(request, ct)
                : network.SendAsync(request, ct);
    }
}
