using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Lantern.Till.Services;

/// <summary>Long-lived cookie naming this browser's till registration. Holds only a random id (spec §6.1).</summary>
public static class DeviceCookie
{
    public const string Name = "lantern.till.device";

    public static string NewId() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static void Write(HttpResponse response, string deviceId) =>
        response.Cookies.Append(Name, deviceId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(400),
            IsEssential = true
        });
}
