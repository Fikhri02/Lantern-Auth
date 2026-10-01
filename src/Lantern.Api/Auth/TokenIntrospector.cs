using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Lantern.Api.Auth;

public enum IntrospectionResult { Active, Inactive, Unavailable }

/// <summary>
/// Asks Keycloak whether a locally valid token is still active, so a deactivated user is locked out
/// within <see cref="KeycloakOptions.IntrospectionCacheSeconds"/> (spec §5.4).
/// </summary>
public sealed class TokenIntrospector(HttpClient http, IMemoryCache cache, IOptions<KeycloakOptions> options,
    ILogger<TokenIntrospector> logger)
{
    public async Task<IntrospectionResult> CheckAsync(JsonWebToken token, CancellationToken ct)
    {
        var kc = options.Value;
        var key = "introspect:" + (string.IsNullOrEmpty(token.Id)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.EncodedToken)))
            : token.Id);
        if (cache.TryGetValue(key, out IntrospectionResult cached)) return cached;

        IntrospectionResult result;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{kc.RealmUrl}/protocol/openid-connect/token/introspect")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = token.EncodedToken,
                    ["client_id"] = kc.ApiClientId,
                    ["client_secret"] = kc.ApiClientSecret
                })
            };
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Introspection returned {Status}", (int)response.StatusCode);
                return IntrospectionResult.Unavailable;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            result = doc.RootElement.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True
                ? IntrospectionResult.Active
                : IntrospectionResult.Inactive;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Introspection request failed");
            return IntrospectionResult.Unavailable;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Introspection request timed out");
            return IntrospectionResult.Unavailable;
        }

        if (result == IntrospectionResult.Inactive)
            cache.Set(key, result, token.ValidTo > DateTime.UtcNow
                ? new DateTimeOffset(token.ValidTo, TimeSpan.Zero)
                : DateTimeOffset.UtcNow.AddMinutes(1));
        else if (kc.IntrospectionCacheSeconds > 0)
            cache.Set(key, result, TimeSpan.FromSeconds(kc.IntrospectionCacheSeconds));

        return result;
    }
}
