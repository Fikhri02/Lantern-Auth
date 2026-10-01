using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Lantern.Till.Services;

/// <summary>The till server's calls to Keycloak's token, revocation and logout endpoints, as the till client.</summary>
public sealed class KeycloakTillClient(IHttpClientFactory httpFactory, IOptions<TillOptions> options)
{
    private TillOptions Options => options.Value;
    private string Oidc => $"{Options.RealmUrl}/protocol/openid-connect";

    public Task<(TokenResponse? Tokens, string? Error)> RefreshAsync(string refreshToken, CancellationToken ct) =>
        TokenAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);

    /// <summary>The PIN request (spec §6.2 step 2), routed by Keycloak to the till-cashier-pin flow.</summary>
    public Task<(TokenResponse? Tokens, string? Error)> CashierPinAsync(string outletAccessToken, string code, string pin, string? newPin, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["scope"] = "openid",
            ["username"] = code,
            ["pin"] = pin,
            ["outlet_token"] = outletAccessToken
        };
        if (newPin is not null) form["new_pin"] = newPin;
        return TokenAsync(form, ct);
    }

    /// <summary>Revokes the outlet's offline login (Sign out this till). Best effort.</summary>
    public Task RevokeAsync(string refreshToken, CancellationToken ct) =>
        PostBestEffortAsync($"{Oidc}/revoke", new Dictionary<string, string>
        {
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token"
        }, ct);

    /// <summary>Ends a cashier's Keycloak session. Best effort.</summary>
    public Task LogoutAsync(string refreshToken, CancellationToken ct) =>
        PostBestEffortAsync($"{Oidc}/logout", new Dictionary<string, string> { ["refresh_token"] = refreshToken }, ct);

    private async Task<(TokenResponse? Tokens, string? Error)> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = Options.ClientId;
        form["client_secret"] = Options.ClientSecret;
        try
        {
            using var response = await httpFactory.CreateClient("keycloak")
                .PostAsync($"{Oidc}/token", new FormUrlEncodedContent(form), ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var body = json.RootElement;
            if (response.IsSuccessStatusCode)
                return (new TokenResponse(
                    body.GetProperty("access_token").GetString()!,
                    body.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
                    body.GetProperty("expires_in").GetInt32()), null);

            var error = body.TryGetProperty("error_description", out var description) ? description.GetString()
                : body.TryGetProperty("error", out var code) ? code.GetString()
                : null;
            return (null, error ?? "unknown_error");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException ||
                                  e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, TillMessages.Unavailable);
        }
    }

    private async Task PostBestEffortAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = Options.ClientId;
        form["client_secret"] = Options.ClientSecret;
        try
        {
            using var _ = await httpFactory.CreateClient("keycloak").PostAsync(url, new FormUrlEncodedContent(form), ct);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Nothing more the till can do; the session will expire on Keycloak's idle timeout.
        }
    }
}
