using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Lantern.BrowserTests;

/// <summary>
/// Sets up temporary users through Keycloak's admin API so tests never change seeded accounts, and deletes them
/// afterwards so the long-lived dev realm doesn't fill up with test users and signed-in test tills.
/// </summary>
public sealed class KeycloakAdmin(string keycloak) : IDisposable
{
    public const string Password = "Lantern!2026";
    private readonly HttpClient _http = new();
    private readonly ConcurrentBag<string> _createdIds = [];
    private readonly ConcurrentBag<string> _createdUsernames = [];

    public async Task<(string Id, string Username)> CreateUserAsync(string groupPath, params string[] realmRoles)
    {
        var username = $"e2e-{Guid.NewGuid():N}"[..16];
        using var admin = await AdminAsync();
        using var create = await admin.PostAsJsonAsync("users", new
        {
            username,
            enabled = true,
            email = $"{username}@lantern.test",
            emailVerified = true,
            firstName = "Test",
            lastName = username,
            groups = new[] { groupPath },
            credentials = new[] { new { type = "password", value = Password, temporary = false } }
        });
        create.EnsureSuccessStatusCode();
        var id = create.Headers.Location!.Segments.Last();
        _createdIds.Add(id);
        foreach (var roleName in realmRoles)
        {
            var role = await admin.GetFromJsonAsync<JsonObject>($"roles/{roleName}");
            using var map = await admin.PostAsJsonAsync($"users/{id}/role-mappings/realm", new[] { role });
            map.EnsureSuccessStatusCode();
        }
        return (id, username);
    }

    public async Task SetPinAsync(string userId, string pin, bool temporary)
    {
        using var token = await _http.PostAsync($"{keycloak}/realms/lantern/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "api-admin-svc",
                ["client_secret"] = "api-admin-svc-dev-secret"
            }));
        token.EnsureSuccessStatusCode();
        var accessToken = (string)JsonNode.Parse(await token.Content.ReadAsStringAsync())!["access_token"]!;
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{keycloak}/realms/lantern/lantern-pin/users/{userId}")
        {
            Content = JsonContent.Create(new { pin, temporary })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogoutUserAsync(string userId)
    {
        using var admin = await AdminAsync();
        using var response = await admin.PostAsync($"users/{userId}/logout", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>For users a test creates through an app's UI rather than through this class.</summary>
    public void DeleteAfterRun(string username) => _createdUsernames.Add(username);

    /// <summary>Deletes every user created during the run; Keycloak drops their sessions, offline ones included.</summary>
    public async Task DeleteCreatedUsersAsync()
    {
        if (_createdIds.IsEmpty && _createdUsernames.IsEmpty) return;
        using var admin = await AdminAsync();
        var ids = _createdIds.ToList();
        foreach (var username in _createdUsernames)
        {
            var found = await admin.GetFromJsonAsync<JsonArray>($"users?username={Uri.EscapeDataString(username)}&exact=true");
            ids.AddRange(found!.Select(u => (string)u!["id"]!));
        }
        foreach (var id in ids)
        {
            using var response = await admin.DeleteAsync($"users/{id}");
            if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
        }
    }

    private async Task<HttpClient> AdminAsync()
    {
        using var token = await _http.PostAsync($"{keycloak}/realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = "admin", ["password"] = "admin"
            }));
        token.EnsureSuccessStatusCode();
        var client = new HttpClient { BaseAddress = new Uri($"{keycloak}/admin/realms/lantern/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (string)JsonNode.Parse(await token.Content.ReadAsStringAsync())!["access_token"]!);
        return client;
    }

    public void Dispose() => _http.Dispose();
}
