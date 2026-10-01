using System.Security.Cryptography;
using System.Text;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// An authenticator app for tests: RFC 6238 TOTP with Keycloak's defaults (HMAC-SHA1, 6 digits, 30 s).
/// Keycloak's key is the UTF-8 bytes of the secret in its setup form. Never hands out the same time step
/// twice, because Keycloak refuses a reused code.
/// </summary>
public sealed class Totp
{
    private long _lastStep = -1;

    public string? Secret { get; set; }

    public string NextCode()
    {
        if (Secret is null) throw new InvalidOperationException("No TOTP secret yet; enrol first.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var step = Math.Max(now, _lastStep + 1);
        if (step > now + 1) // beyond Keycloak's one-step look-ahead: wait for the next window
        {
            Thread.Sleep(TimeSpan.FromSeconds(30 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 + 1));
            return NextCode();
        }
        _lastStep = step;
        return Code(step);
    }

    private string Code(long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(Encoding.UTF8.GetBytes(Secret!), counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }
}
