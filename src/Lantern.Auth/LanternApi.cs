using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Lantern.Auth;

public sealed record ApiResult<T>(int Status, T? Value, string? Policy)
{
    public bool Ok => Status is >= 200 and < 300;
}

/// <summary>Calls Lantern.Api as the signed-in user (spec §5.3). Returns the status rather than throwing.</summary>
public sealed class LanternApi(IHttpClientFactory http, AccessTokenProvider tokens, IOptions<LanternBffOptions> options)
{
    public Task<ApiResult<T>> GetAsync<T>(ClaimsPrincipal user, string path, CancellationToken ct = default) =>
        SendAsync<T>(user, HttpMethod.Get, path, null, ct);

    public Task<ApiResult<T>> PostAsync<T>(ClaimsPrincipal user, string path, object? body, CancellationToken ct = default) =>
        SendAsync<T>(user, HttpMethod.Post, path, body, ct);

    private async Task<ApiResult<T>> SendAsync<T>(ClaimsPrincipal user, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = await tokens.GetAccessTokenAsync(user, ct);
        if (token is null) return new ApiResult<T>(401, default, null);

        using var request = new HttpRequestMessage(method, $"{options.Value.ApiBaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.CreateClient(LanternBffSetup.ApiClient).SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return new ApiResult<T>(status, response.Content.Headers.ContentLength == 0 ? default : await response.Content.ReadFromJsonAsync<T>(ct), null);

            string? policy = null;
            if (status == 403 && response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
            {
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                policy = problem.RootElement.TryGetProperty("policy", out var p) ? p.GetString() : null;
            }
            return new ApiResult<T>(status, default, policy);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ApiResult<T>(503, default, null);
        }
    }
}
