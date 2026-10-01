namespace Lantern.Auth;

public sealed class LanternBffOptions
{
    public const string Section = "Lantern";

    public string KeycloakBaseUrl { get; set; } = "http://localhost:8080";
    public string Realm { get; set; } = "lantern";
    /// <summary>Exact iss in tokens; differs from KeycloakBaseUrl inside Docker (spec §8).</summary>
    public string Issuer { get; set; } = "http://localhost:8080/realms/lantern";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "http://localhost:5100";
    /// <summary>Distinct per app: browsers don't separate cookies by port on localhost (spec §5.1).</summary>
    public string CookieName { get; set; } = "lantern.app";

    public string RealmUrl => $"{KeycloakBaseUrl.TrimEnd('/')}/realms/{Realm}";
}
