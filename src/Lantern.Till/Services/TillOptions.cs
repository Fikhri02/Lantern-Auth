namespace Lantern.Till.Services;

public sealed class TillOptions
{
    public const string Section = "Till";

    /// <summary>Back-channel base URL for Keycloak (e.g. http://keycloak:8080 inside Docker).</summary>
    public string KeycloakBaseUrl { get; set; } = "http://localhost:8080";
    public string Realm { get; set; } = "lantern";
    /// <summary>Exact iss in tokens; differs from KeycloakBaseUrl inside Docker (spec §8).</summary>
    public string Issuer { get; set; } = "http://localhost:8080/realms/lantern";
    public string ClientId { get; set; } = "till";
    public string ClientSecret { get; set; } = "till-dev-secret";
    public string ApiBaseUrl { get; set; } = "http://localhost:5100";
    /// <summary>Holds till.db and the Data Protection keys; a Docker volume in compose.</summary>
    public string DataDirectory { get; set; } = "data";
    public int IdleMinutes { get; set; } = 10;

    public string RealmUrl => $"{KeycloakBaseUrl.TrimEnd('/')}/realms/{Realm}";
}
