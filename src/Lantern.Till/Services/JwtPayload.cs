using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Lantern.Till.Services;

/// <summary>Reads claims from a token Keycloak just returned to us over the back channel; no validation needed.</summary>
public static class JwtPayload
{
    public static JsonElement Read(string jwt) =>
        JsonDocument.Parse(WebEncoders.Base64UrlDecode(jwt.Split('.')[1])).RootElement.Clone();
}
