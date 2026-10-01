using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lantern.Auth;

/// <summary>OIDC Back-Channel Logout 1.0 §2.6 checks for Keycloak's logout_token (spec §5.2).</summary>
public sealed class LogoutTokenValidator(IOptionsMonitor<OpenIdConnectOptions> oidc, IOptions<LanternBffOptions> bff)
{
    private const string BackchannelEvent = "http://schemas.openid.net/event/backchannel-logout";

    /// <returns>The Keycloak session id to end, or null when the token must be refused.</returns>
    public async Task<string?> ValidateAsync(string? logoutToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logoutToken)) return null;

        var options = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(ct);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(logoutToken, new TokenValidationParameters
        {
            ValidIssuer = bff.Value.Issuer,
            ValidAudience = bff.Value.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            RequireExpirationTime = false,
            ValidateLifetime = true
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token) return null;

        var hasEvent = token.TryGetPayloadValue<Dictionary<string, object>>("events", out var events) && events.ContainsKey(BackchannelEvent);
        var hasNonce = token.TryGetPayloadValue<string>("nonce", out _);
        var hasSid = token.TryGetPayloadValue<string>("sid", out var sid) && !string.IsNullOrEmpty(sid);
        return hasEvent && !hasNonce && hasSid ? sid : null;
    }
}
