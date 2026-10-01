using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lantern.Api.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lantern.Api.Keycloak;

/// <summary>Keycloak Admin API calls made as the api-admin-svc service account (spec §5.5).</summary>
public sealed class KeycloakAdminClient(HttpClient http, IOptions<KeycloakOptions> options, IMemoryCache cache)
{
    private const string TokenCacheKey = "kc-admin-token";
    private const string ServiceAccountPrefix = "service-account-";
    private KeycloakOptions Kc => options.Value;

    public async Task<IReadOnlyList<StaffMember>> ListStaffAsync(CancellationToken ct)
    {
        var users = await GetJsonAsync<List<KcUser>>("users?briefRepresentation=true&max=500", ct);
        var result = new List<StaffMember>(users.Count);
        foreach (var u in users)
        {
            var groups = await GetJsonAsync<List<KcGroup>>($"users/{u.Id}/groups", ct);
            if (!groups.Any(g => Departments.IsHqPath(g.Path))) continue;
            var roles = await GetJsonAsync<List<KcRole>>($"users/{u.Id}/role-mappings/realm/composite", ct);
            result.Add(new StaffMember(
                u.Id, u.Username, $"{u.FirstName} {u.LastName}".Trim(), u.Email, u.Enabled,
                groups.Select(g => g.Path).Order().ToList(),
                roles.Select(r => r.Name).Where(Roles.All.Contains).Order().ToList()));
        }
        return result;
    }

    public async Task<KcUser?> GetUserAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"users/{Uri.EscapeDataString(id)}", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<KcUser>(ct);
    }

    public static bool IsServiceAccount(KcUser user) =>
        user.Username.StartsWith(ServiceAccountPrefix, StringComparison.Ordinal);

    public async Task<bool> IsHqMemberAsync(string id, CancellationToken ct) =>
        (await GetJsonAsync<List<KcGroup>>($"users/{id}/groups", ct)).Any(g => Departments.IsHqPath(g.Path));

    public async Task<CreateStaffResult> CreateStaffAsync(NewStaff staff, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "users", new
        {
            username = staff.Username,
            email = staff.Email,
            firstName = staff.FirstName,
            lastName = staff.LastName,
            enabled = true,
            emailVerified = true,
            groups = staff.Departments!.Select(Departments.ToGroupPath).ToArray()
        }, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return new CreateStaffResult(null, true, null, false);
        if (response.StatusCode == HttpStatusCode.BadRequest)
            return new CreateStaffResult(null, false, await ReadValidationErrorAsync(response, ct), false);
        response.EnsureSuccessStatusCode();

        var id = response.Headers.Location!.Segments.Last();
        try
        {
            await SendActionsEmailAsync(id, ["UPDATE_PASSWORD"], ct);
            return new CreateStaffResult(id, false, null, true);
        }
        catch (HttpRequestException)
        {
            // The account exists; report it so the admin can resend instead of retrying the create.
            return new CreateStaffResult(id, false, null, false);
        }
    }

    private static async Task<Dictionary<string, string[]>> ReadValidationErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var field = body.TryGetProperty("field", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()! : "user";
        var message = body.TryGetProperty("errorMessage", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()!
            : "Keycloak rejected this user.";
        return new Dictionary<string, string[]> { [field] = [message] };
    }

    public async Task SetDepartmentsAsync(string id, IReadOnlyCollection<string> departments, CancellationToken ct)
    {
        var current = await GetJsonAsync<List<KcGroup>>($"users/{id}/groups", ct);
        var wanted = departments.Select(Departments.ToGroupPath).ToHashSet();

        // Add before removing, so a failure partway never leaves the user with no department.
        foreach (var path in wanted.Where(p => current.All(g => g.Path != p)))
        {
            var group = await GetJsonAsync<KcGroup>($"group-by-path/{path.TrimStart('/')}", ct);
            await SendOkAsync(HttpMethod.Put, $"users/{id}/groups/{group.Id}", null, ct);
        }

        foreach (var group in current.Where(g => Departments.IsDepartmentPath(g.Path) && !wanted.Contains(g.Path)))
            await SendOkAsync(HttpMethod.Delete, $"users/{id}/groups/{group.Id}", null, ct);
    }

    /// <summary>Disable, end online sessions, and revoke till offline sessions (spec §5.4).</summary>
    public async Task DeactivateAsync(string id, CancellationToken ct)
    {
        await SetEnabledAsync(id, false, ct);
        await SendOkAsync(HttpMethod.Post, $"users/{id}/logout", null, ct);
        using var revoke = await SendAsync(HttpMethod.Delete, $"users/{id}/consents/till", null, ct);
        if (revoke.StatusCode != HttpStatusCode.NotFound) revoke.EnsureSuccessStatusCode();
    }

    public Task ReactivateAsync(string id, CancellationToken ct) => SetEnabledAsync(id, true, ct);

    public Task SendActionsEmailAsync(string id, string[] actions, CancellationToken ct) =>
        SendOkAsync(HttpMethod.Put, $"users/{id}/execute-actions-email", actions, ct);

    private async Task SetEnabledAsync(string id, bool enabled, CancellationToken ct)
    {
        // Send the full representation back: partial PUTs can trip user-profile validation.
        var user = await GetJsonAsync<JsonObject>($"users/{id}", ct);
        user["enabled"] = enabled;
        await SendOkAsync(HttpMethod.Put, $"users/{id}", user, ct);
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private async Task SendOkAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{Kc.AdminUrl}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetServiceTokenAsync(ct));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, ct);
    }

    private async Task<string> GetServiceTokenAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null) return cached;

        using var response = await http.PostAsync($"{Kc.RealmUrl}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = Kc.AdminClientId,
                ["client_secret"] = Kc.AdminClientSecret
            }), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var token = json.GetProperty("access_token").GetString()!;
        var lifetime = Math.Max(json.GetProperty("expires_in").GetInt32() - 30, 5);
        cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(lifetime));
        return token;
    }
}
