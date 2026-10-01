using System.Net;
using System.Text.RegularExpressions;

namespace Lantern.IntegrationTests.Infrastructure;

public sealed record BrowserPage(Uri Url, int Status, string Html);

/// <summary>
/// A browser for the till: requests to http://localhost:5400 go to the in-memory Till, everything else
/// (Keycloak) goes over the network. Follows redirects across both, keeping cookies per origin.
/// </summary>
public sealed partial class TillBrowser : IDisposable
{
    public const string TillOrigin = "http://localhost:5400";

    private readonly LocalhostCookieJar _jar;
    private readonly HttpClient _http;

    public TillBrowser(TillFactory till)
    {
        _jar = new LocalhostCookieJar(new OriginRouter(till.Server.CreateHandler(),
            new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }));
        _http = new HttpClient(_jar);
    }

    public string? DeviceId => _jar.Get(TillOrigin, "lantern.till.device");

    public Task<BrowserPage> GetAsync(string pathOrUrl) =>
        FollowAsync(new HttpRequestMessage(HttpMethod.Get, Absolute(pathOrUrl)));

    /// <summary>Clicks "Sign in this till", fills in Keycloak's form, and follows every redirect back.</summary>
    public async Task<BrowserPage> RegisterAsync(string account, string password = KeycloakFixture.DemoPassword)
    {
        var login = await GetAsync("/register");
        var action = LoginAction(login.Html)
                     ?? throw new InvalidOperationException($"Expected Keycloak's login form at {login.Url}, got {login.Status}");
        return await FollowAsync(new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = account, ["password"] = password, ["credentialId"] = ""
            })
        });
    }

    public void Dispose() => _http.Dispose();

    private async Task<BrowserPage> FollowAsync(HttpRequestMessage request)
    {
        for (var hop = 0; hop < 20; hop++)
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

    // "/register" parses as an absolute file: URI on Unix, so only http(s) counts as absolute here.
    private static Uri Absolute(string pathOrUrl) =>
        pathOrUrl.StartsWith("http://", StringComparison.Ordinal) || pathOrUrl.StartsWith("https://", StringComparison.Ordinal)
            ? new Uri(pathOrUrl)
            : new Uri(new Uri(TillOrigin), pathOrUrl);

    [GeneratedRegex("<form\\b[^>]*\\bid=\"kc-form-login\"[^>]*>")]
    private static partial Regex LoginFormTag();

    [GeneratedRegex("\\baction=\"([^\"]+)\"")]
    private static partial Regex ActionAttribute();

    private static Uri? LoginAction(string html)
    {
        var tag = LoginFormTag().Match(html);
        if (!tag.Success) return null;
        var action = ActionAttribute().Match(tag.Value);
        return action.Success ? new Uri(WebUtility.HtmlDecode(action.Groups[1].Value)) : null;
    }

    private sealed class OriginRouter(HttpMessageHandler till, HttpMessageHandler network) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _till = new(till);
        private readonly HttpMessageInvoker _network = new(network);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            request.RequestUri!.GetLeftPart(UriPartial.Authority) == TillOrigin
                ? _till.SendAsync(request, ct)
                : _network.SendAsync(request, ct);
    }
}
