using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Testcontainers.Keycloak;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// One Keycloak (our image, our realm) and one Mailpit for the whole test run.
/// Adds a test-only password-grant client so tests can get user tokens without a browser.
/// </summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
    public const string DemoPassword = "Lantern!2026";
    private const string TestClientId = "test-runner";
    private const string TestClientSecret = "test-runner-secret";

    private readonly Dictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokens = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private INetwork _network = null!;
    private IFutureDockerImage _image = null!;
    private KeycloakContainer _keycloak = null!;
    private IContainer _mailpit = null!;

    public string BaseUrl { get; private set; } = "";
    public string Issuer => $"{BaseUrl}/realms/lantern";
    public string MailpitUrl { get; private set; } = "";
    public ApiFactory Api { get; private set; } = null!;
    public HttpClient Http { get; } = new();

    public async Task InitializeAsync()
    {
        var repoRoot = CommonDirectoryPath.GetGitDirectory();

        _image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(repoRoot, "keycloak")
            .WithDockerfile("Dockerfile")
            .WithName("lantern-keycloak-test:26.7.5") // version tag lets Testcontainers pick the 26.x health port
            .WithCleanUp(false)
            .Build();
        await _image.CreateAsync();

        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        _mailpit = new ContainerBuilder("axllent/mailpit:v1.31.3")
            .WithNetwork(_network)
            .WithNetworkAliases("mailpit")
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/livez")))
            .Build();
        await _mailpit.StartAsync();
        MailpitUrl = $"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}";

        var realmPath = Path.Combine(repoRoot.DirectoryPath, "keycloak", "realm", "lantern-realm.json");
        _keycloak = new KeycloakBuilder(_image.FullName)
            .WithNetwork(_network)
            .WithResourceMapping(Encoding.UTF8.GetBytes(BuildTestRealm(realmPath)), "/opt/keycloak/data/import/lantern-realm.json")
            .WithCommand("--import-realm")
            .Build();
        await _keycloak.StartAsync();
        BaseUrl = _keycloak.GetBaseAddress().TrimEnd('/');
        await AllowPlainHttpOnMasterRealmAsync();

        Api = new ApiFactory(this, introspectionCacheSeconds: 0);
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await _keycloak.DisposeAsync();
        await _mailpit.DisposeAsync();
        await _network.DisposeAsync();
        Http.Dispose();
    }

    /// <summary>
    /// Host-to-container traffic can arrive from a non-private address (VPNs, Docker Desktop
    /// networking), which sslRequired=external rejects; tests must not depend on that.
    /// kcadm runs inside the container, where localhost is always allowed.
    /// </summary>
    private async Task AllowPlainHttpOnMasterRealmAsync()
    {
        const string kcadm = "/opt/keycloak/bin/kcadm.sh";
        const string config = "/tmp/kcadm.config";
        var login = await _keycloak.ExecAsync([kcadm, "config", "credentials", "--config", config,
            "--server", "http://localhost:8080", "--realm", "master",
            "--user", KeycloakBuilder.DefaultUsername, "--password", KeycloakBuilder.DefaultPassword]);
        var update = await _keycloak.ExecAsync([kcadm, "update", "realms/master", "--config", config, "-s", "sslRequired=NONE"]);
        if (login.ExitCode != 0 || update.ExitCode != 0)
            throw new InvalidOperationException($"kcadm failed: {login.Stderr} {update.Stderr}");
    }

    private static string BuildTestRealm(string path)
    {
        var realm = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        realm["sslRequired"] = "none";
        var clients = realm["clients"]!.AsArray();
        var mappers = clients.First(c => (string?)c!["clientId"] == "backoffice")!["protocolMappers"]!.DeepClone();
        clients.Add(new JsonObject
        {
            ["clientId"] = TestClientId,
            ["enabled"] = true,
            ["publicClient"] = false,
            ["secret"] = TestClientSecret,
            ["standardFlowEnabled"] = false,
            ["directAccessGrantsEnabled"] = true,
            ["protocolMappers"] = mappers
        });
        return realm.ToJsonString();
    }

    /// <summary>Cached per user; refreshed when under 30 s from expiry.</summary>
    public async Task<string> GetUserTokenAsync(string username, string password = DemoPassword)
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_tokens.TryGetValue(username, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
                return cached.Token;
            var fresh = await RequestPasswordTokenAsync(username, password);
            _tokens[username] = fresh;
            return fresh.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<string> GetFreshUserTokenAsync(string username, string password = DemoPassword) =>
        (await RequestPasswordTokenAsync(username, password)).Token;

    private async Task<(string Token, DateTimeOffset ExpiresAt)> RequestPasswordTokenAsync(string username, string password)
    {
        using var response = await Http.PostAsync($"{Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = TestClientId,
                ["client_secret"] = TestClientSecret,
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid"
            }));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token request for {username} failed: {(int)response.StatusCode} {body}");
        var json = JsonNode.Parse(body)!;
        return ((string)json["access_token"]!, DateTimeOffset.UtcNow.AddSeconds((int)json["expires_in"]!));
    }

    /// <summary>Admin API client for the lantern realm, authenticated as the master admin.</summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        using var response = await Http.PostAsync($"{BaseUrl}/realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = KeycloakBuilder.DefaultUsername,
                ["password"] = KeycloakBuilder.DefaultPassword
            }));
        response.EnsureSuccessStatusCode();
        var token = (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["access_token"]!;
        var client = new HttpClient { BaseAddress = new Uri($"{BaseUrl}/admin/realms/lantern/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<(string Id, string Username, string Email)> CreateTempUserAsync(string groupPath, string password = DemoPassword)
    {
        var username = $"temp-{Guid.NewGuid():N}"[..20];
        var email = $"{username}@lantern.test";
        using var admin = await AdminClientAsync();
        using var create = await admin.PostAsJsonAsync("users", new
        {
            username,
            enabled = true,
            email,
            emailVerified = true,
            firstName = "Temp",
            lastName = "User",
            groups = new[] { groupPath },
            credentials = new[] { new { type = "password", value = password, temporary = false } }
        });
        create.EnsureSuccessStatusCode();
        return (create.Headers.Location!.Segments.Last(), username, email);
    }

    public async Task<string> GetUserIdAsync(string username)
    {
        using var admin = await AdminClientAsync();
        var users = await admin.GetFromJsonAsync<JsonElement>($"users?username={Uri.EscapeDataString(username)}&exact=true");
        return users.EnumerateArray().Single().GetProperty("id").GetString()!;
    }

    /// <summary>A user with no groups, roles or credentials, e.g. to simulate a half-created account.</summary>
    public async Task<string> CreateBareUserAsync(string username)
    {
        using var admin = await AdminClientAsync();
        using var create = await admin.PostAsJsonAsync("users", new
        {
            username, enabled = true, email = $"{username}@lantern.test", emailVerified = true, firstName = "Bare", lastName = "User"
        });
        create.EnsureSuccessStatusCode();
        return create.Headers.Location!.Segments.Last();
    }

    public async Task SetUserEnabledAsync(string id, bool enabled)
    {
        using var admin = await AdminClientAsync();
        var user = (await admin.GetFromJsonAsync<JsonObject>($"users/{id}"))!;
        user["enabled"] = enabled;
        using var put = await admin.PutAsJsonAsync($"users/{id}", user);
        put.EnsureSuccessStatusCode();
    }

    public async Task LogoutUserAsync(string id)
    {
        using var admin = await AdminClientAsync();
        using var response = await admin.PostAsync($"users/{id}/logout", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<(string Id, string Username)> CreateTempTillAsync(string outletGroupPath)
    {
        var (id, username, _) = await CreateTempUserAsync(outletGroupPath);
        using var admin = await AdminClientAsync();
        var role = await admin.GetFromJsonAsync<JsonObject>("roles/outlet-device");
        using var map = await admin.PostAsJsonAsync($"users/{id}/role-mappings/realm", new[] { role });
        map.EnsureSuccessStatusCode();
        return (id, username);
    }

    /// <summary>Signs a till in as an outlet account; like the till, then ends the online session.</summary>
    public async Task<TokenSet> OutletLoginAsync(string username, string password = DemoPassword, bool endOnlineSession = true)
    {
        using var browser = new OidcBrowser(Issuer);
        var outcome = await browser.LoginAsync(username, password);
        if (!outcome.Succeeded)
            throw new InvalidOperationException($"Outlet login for {username} refused ({outcome.Status}): {OidcBrowser.Snippet(outcome.Html!)}");
        if (endOnlineSession) await browser.EndOnlineSessionAsync(outcome.Tokens!.IdToken);
        return outcome.Tokens!;
    }

    public async Task<(HttpStatusCode Status, JsonNode Body)> RefreshTillTokenAsync(string refreshToken)
    {
        using var response = await Http.PostAsync($"{Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = OidcBrowser.TillClientId,
                ["client_secret"] = OidcBrowser.TillClientSecret
            }));
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    public async Task<string> GetTillClientUuidAsync()
    {
        using var admin = await AdminClientAsync();
        var clients = await admin.GetFromJsonAsync<JsonElement>("clients?clientId=till");
        return clients.EnumerateArray().Single().GetProperty("id").GetString()!;
    }

    public async Task RevokeTillLoginAsync(string userId)
    {
        using var admin = await AdminClientAsync();
        using var response = await admin.DeleteAsync($"users/{userId}/consents/till");
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> ServiceTokenAsync()
    {
        using var response = await Http.PostAsync($"{Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "api-admin-svc",
                ["client_secret"] = "api-admin-svc-dev-secret"
            }));
        response.EnsureSuccessStatusCode();
        return (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["access_token"]!;
    }

    public async Task SetPinAsync(string userId, string pin, bool temporary)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Issuer}/lantern-pin/users/{userId}")
        {
            Content = JsonContent.Create(new { pin, temporary })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await ServiceTokenAsync());
        using var response = await Http.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Setting PIN failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    public async Task<(string Id, string Username)> CreateTempCashierAsync(string outletGroupPath, string? pin = null, bool temporary = false)
    {
        var (id, username, _) = await CreateTempUserAsync(outletGroupPath);
        using var admin = await AdminClientAsync();
        var role = await admin.GetFromJsonAsync<JsonObject>("roles/cashier");
        using var map = await admin.PostAsJsonAsync($"users/{id}/role-mappings/realm", new[] { role });
        map.EnsureSuccessStatusCode();
        if (pin is not null) await SetPinAsync(id, pin, temporary);
        return (id, username);
    }

    /// <summary>A fresh temporary till account in the outlet, signed in the way the till does it.</summary>
    public async Task<OpenTill> OpenTillAsync(string outletGroupPath)
    {
        var (id, account) = await CreateTempTillAsync(outletGroupPath);
        var tokens = await OutletLoginAsync(account);
        var (status, body) = await RefreshTillTokenAsync(tokens.RefreshToken);
        if (status != HttpStatusCode.OK) throw new InvalidOperationException($"Outlet refresh failed: {body}");
        return new OpenTill(id, account, tokens.RefreshToken, (string)body["access_token"]!);
    }

    /// <summary>The till server's PIN request: password grant on the till client, routed to till-cashier-pin.</summary>
    public async Task<(HttpStatusCode Status, JsonNode Body)> CashierPinAsync(string? outletToken, string username, string? pin, string? newPin = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = OidcBrowser.TillClientId,
            ["client_secret"] = OidcBrowser.TillClientSecret,
            ["username"] = username,
            ["scope"] = "openid"
        };
        if (outletToken is not null) form["outlet_token"] = outletToken;
        if (pin is not null) form["pin"] = pin;
        if (newPin is not null) form["new_pin"] = newPin;
        using var response = await Http.PostAsync($"{Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(form));
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    public static string? ErrorCode(JsonNode body) => (string?)body["error_description"];

    public async Task<string> CashierTokenAsync(OpenTill till, string username, string pin)
    {
        var (status, body) = await CashierPinAsync(till.AccessToken, username, pin);
        if (status != HttpStatusCode.OK) throw new InvalidOperationException($"PIN sign-in for {username} failed: {body}");
        return (string)body["access_token"]!;
    }

    public async Task<int> WaitForEmailCountAsync(string email, int atLeast, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        var count = 0;
        while (DateTime.UtcNow < deadline)
        {
            var json = await Http.GetFromJsonAsync<JsonElement>(
                $"{MailpitUrl}/api/v1/search?query={Uri.EscapeDataString("to:" + email)}");
            count = json.GetProperty("messages_count").GetInt32();
            if (count >= atLeast) return count;
            await Task.Delay(500);
        }
        return count;
    }
}
