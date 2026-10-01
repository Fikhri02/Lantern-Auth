using System.Security.Cryptography;

namespace Lantern.Api.Keycloak;

public static class PinGenerator
{
    /// <summary>A random 6-digit temporary PIN; the cashier replaces it at first sign-in (spec §6.4).</summary>
    public static string NewTemporaryPin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
