namespace Lantern.Till.Services;

/// <summary>A till signed in as an outlet account (spec §6.1). RefreshToken is the offline token.</summary>
public sealed record TillRegistration(string DeviceId, string Account, string OutletId, string RefreshToken, DateTimeOffset RegisteredAt);
