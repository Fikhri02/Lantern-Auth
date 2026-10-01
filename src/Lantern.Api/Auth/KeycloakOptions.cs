namespace Lantern.Api.Auth;

public sealed class KeycloakOptions
{
    public const string Section = "Keycloak";

    /// <summary>Back-channel base URL the API uses to reach Keycloak (e.g. http://keycloak:8080).</summary>
    public string BaseUrl { get; set; } = "";
    public string Realm { get; set; } = "lantern";
    /// <summary>Exact iss value in tokens; differs from BaseUrl inside Docker (spec §8).</summary>
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "lantern-api";
    public string ApiClientId { get; set; } = "lantern-api";
    public string ApiClientSecret { get; set; } = "";
    public string AdminClientId { get; set; } = "api-admin-svc";
    public string AdminClientSecret { get; set; } = "";
    public int IntrospectionCacheSeconds { get; set; } = 15;
    /// <summary>Internal id of the till client, fixed in the realm file so the API needs no client-read rights.</summary>
    public string TillClientUuid { get; set; } = "";

    public string RealmUrl => $"{BaseUrl.TrimEnd('/')}/realms/{Realm}";
    public string AdminUrl => $"{BaseUrl.TrimEnd('/')}/admin/realms/{Realm}";
}
