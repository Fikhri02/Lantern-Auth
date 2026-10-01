using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// Drives Keycloak's browser login for the till client over plain HTTP: authorization code + PKCE,
/// with a cookie jar, the way a till's browser and server would together.
/// </summary>
public sealed partial class OidcBrowser(string issuer) : IDisposable
{
    public const string TillClientId = "till";
    public const string TillClientSecret = "till-dev-secret";
    public const string TillRedirectUri = "http://localhost:5400/signin-oidc";
    public const string TillPostLogoutUri = "http://localhost:5400/signed-out";

    private readonly HttpClient _http = new(new LocalhostCookieJar(new HttpClientHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false
    }));

    [GeneratedRegex("<form\\b[^>]*\\bid=\"kc-form-login\"[^>]*>")]
    private static partial Regex LoginFormTag();

    [GeneratedRegex("\\baction=\"([^\"]+)\"")]
    private static partial Regex ActionAttribute();

    public async Task<LoginOutcome> LoginAsync(string username, string password, string scope = "openid offline_access")
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = $"{issuer}/protocol/openid-connect/auth?client_id={TillClientId}&response_type=code" +
                        $"&redirect_uri={Uri.EscapeDataString(TillRedirectUri)}&scope={Uri.EscapeDataString(scope)}" +
                        $"&state={Guid.NewGuid():N}&code_challenge={challenge}&code_challenge_method=S256";

        using var page = await _http.GetAsync(authorize);
        string? redirect;
        if (IsRedirectToTill(page, out redirect))
            return await ExchangeAsync(redirect!, verifier); // SSO cookie skipped the form

        var html = await page.Content.ReadAsStringAsync();
        var action = FindLoginAction(html)
                     ?? throw new InvalidOperationException($"No login form ({(int)page.StatusCode}): {Snippet(html)}");

        using var submitted = await _http.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["credentialId"] = ""
        }));
        if (!IsRedirectToTill(submitted, out redirect))
            return LoginOutcome.Refused((int)submitted.StatusCode, await submitted.Content.ReadAsStringAsync());

        return await ExchangeAsync(redirect!, verifier);
    }

    /// <summary>RP-initiated logout of the browser's online session, as the till does right after registering.</summary>
    public async Task EndOnlineSessionAsync(string idToken)
    {
        using var response = await _http.GetAsync(
            $"{issuer}/protocol/openid-connect/logout?id_token_hint={Uri.EscapeDataString(idToken)}" +
            $"&post_logout_redirect_uri={Uri.EscapeDataString(TillPostLogoutUri)}");
        if (response.StatusCode != HttpStatusCode.Found)
            throw new InvalidOperationException($"Logout did not redirect ({(int)response.StatusCode}): {Snippet(await response.Content.ReadAsStringAsync())}");
    }

    public static string Snippet(string html)
    {
        var text = Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), "\\s+", " ").Trim();
        return text.Length > 300 ? text[..300] : text;
    }

    public void Dispose() => _http.Dispose();

    private async Task<LoginOutcome> ExchangeAsync(string redirect, string verifier)
    {
        var code = HttpUtility.ParseQueryString(new Uri(redirect).Query)["code"]
                   ?? throw new InvalidOperationException($"No code in redirect: {redirect}");
        using var response = await _http.PostAsync($"{issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = TillRedirectUri,
                ["client_id"] = TillClientId,
                ["client_secret"] = TillClientSecret,
                ["code_verifier"] = verifier
            }));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Code exchange failed ({(int)response.StatusCode}): {body}");
        var json = JsonNode.Parse(body)!;
        return LoginOutcome.Success(new TokenSet((string)json["access_token"]!, (string)json["refresh_token"]!, (string)json["id_token"]!));
    }

    private static bool IsRedirectToTill(HttpResponseMessage response, out string? location)
    {
        location = response.Headers.Location?.ToString();
        return response.StatusCode == HttpStatusCode.Found && location is not null &&
               location.StartsWith(TillRedirectUri, StringComparison.Ordinal);
    }

    private static string? FindLoginAction(string html)
    {
        var tag = LoginFormTag().Match(html);
        if (!tag.Success) return null;
        var action = ActionAttribute().Match(tag.Value);
        return action.Success ? WebUtility.HtmlDecode(action.Groups[1].Value) : null;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

}
