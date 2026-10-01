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
public sealed class AccessTokenProvider(ServerSessionStore sessions, IHttpClientFactory http, IOptions<LanternBffOptions> options, TimeProvider time)
{
    public async Task<string?> GetAccessTokenAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.FindFirstValue(ServerSessionStore.SessionClaim) is not { } key) return null;
        if (await sessions.RetrieveAsync(key) is not { } ticket) return null;

        var properties = ticket.Properties;
        var access = properties.GetTokenValue("access_token");
        var expiresAt = DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var at) ? at : DateTimeOffset.MinValue;
        if (access is not null && expiresAt - time.GetUtcNow() > TimeSpan.FromSeconds(30)) return access;

        var refreshed = await RefreshAsync(properties.GetTokenValue("refresh_token"), ct);
        if (refreshed is null)
        {
            await sessions.RemoveAsync(key);
            return null;
        }

        properties.UpdateTokenValue("access_token", refreshed.Value.Access);
        if (refreshed.Value.Refresh is { } refresh) properties.UpdateTokenValue("refresh_token", refresh);
        properties.UpdateTokenValue("expires_at",
            time.GetUtcNow().AddSeconds(refreshed.Value.ExpiresIn).ToString("o", CultureInfo.InvariantCulture));
        await sessions.RenewAsync(key, ticket);
        return refreshed.Value.Access;
    }

    private async Task<(string Access, string? Refresh, int ExpiresIn)?> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) return null;
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
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var body = json.RootElement;
            return (body.GetProperty("access_token").GetString()!,
                body.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
                body.GetProperty("expires_in").GetInt32());
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
