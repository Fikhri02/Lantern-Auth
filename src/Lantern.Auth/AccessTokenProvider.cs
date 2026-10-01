using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Lantern.Auth;

/// <summary>
/// The signed-in user's access token, read from the server session and refreshed when near expiry.
/// A failed refresh (session ended at Keycloak) removes the server session, so the next page load signs in.
/// </summary>
/// <summary>What happened when the app asked for the user's access token.</summary>
public enum TokenOutcome
{
    Ok,
    /// <summary>Keycloak refused the refresh (or there's no session): the server session has been removed.</summary>
    SessionEnded,
    /// <summary>Keycloak couldn't be reached or failed: the session is kept, try again later.</summary>
    Unavailable
}

/// <summary>
/// The signed-in user's access token, read from the server session and refreshed when near expiry.
/// Only a refused refresh (400 invalid_grant) ends the session; a Keycloak outage never signs anyone out.
/// </summary>
public sealed class AccessTokenProvider(ServerSessionStore sessions, IHttpClientFactory http, IOptions<LanternBffOptions> options, TimeProvider time)
{
    public async Task<string?> GetAccessTokenAsync(ClaimsPrincipal user, CancellationToken ct) => (await GetAsync(user, ct)).Token;

    public async Task<(TokenOutcome Outcome, string? Token)> GetAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.FindFirstValue(ServerSessionStore.SessionClaim) is not { } key) return (TokenOutcome.SessionEnded, null);
        if (await sessions.RetrieveAsync(key) is not { } ticket) return (TokenOutcome.SessionEnded, null);

        var properties = ticket.Properties;
        var access = properties.GetTokenValue("access_token");
        var expiresAt = DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var at) ? at : DateTimeOffset.MinValue;
        if (access is not null && expiresAt - time.GetUtcNow() > TimeSpan.FromSeconds(30)) return (TokenOutcome.Ok, access);

        var (refreshed, rejected) = await RefreshAsync(properties.GetTokenValue("refresh_token"), ct);
        if (refreshed is null)
        {
            if (!rejected) return (TokenOutcome.Unavailable, null);
            await sessions.RemoveAsync(key);
            return (TokenOutcome.SessionEnded, null);
        }

        properties.UpdateTokenValue("access_token", refreshed.Value.Access);
        if (refreshed.Value.Refresh is { } refresh) properties.UpdateTokenValue("refresh_token", refresh);
        properties.UpdateTokenValue("expires_at",
            time.GetUtcNow().AddSeconds(refreshed.Value.ExpiresIn).ToString("o", CultureInfo.InvariantCulture));
        await sessions.RenewAsync(key, ticket);
        return (TokenOutcome.Ok, refreshed.Value.Access);
    }

    private async Task<((string Access, string? Refresh, int ExpiresIn)? Tokens, bool Rejected)> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) return (null, true);
        var o = options.Value;
        try
        {
            using var response = await http.CreateClient(LanternBffSetup.KeycloakClient).PostAsync(
                $"{o.RealmUrl}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = o.ClientId,
                    ["client_secret"] = o.ClientSecret
                }), ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var body = json.RootElement;
            if (!response.IsSuccessStatusCode)
            {
                var rejected = response.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                               body.TryGetProperty("error", out var error) && error.GetString() == "invalid_grant";
                return (null, rejected);
            }
            return ((body.GetProperty("access_token").GetString()!,
                body.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
                body.GetProperty("expires_in").GetInt32()), false);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, false);
        }
    }
}
