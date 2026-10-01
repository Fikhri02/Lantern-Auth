using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Lantern.Till.Services;

public sealed class LanternApiClient(IHttpClientFactory httpFactory, IOptions<TillOptions> options)
{
    public async Task<(Receipt? Receipt, int Status)> RingSaleAsync(string outletId, string cashierAccessToken,
        IReadOnlyList<SaleItem> items, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.ApiBaseUrl.TrimEnd('/')}/outlets/{outletId}/sales")
        {
            Content = JsonContent.Create(new { items = items.Select(i => new { sku = i.Sku, quantity = i.Quantity }) })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierAccessToken);
        try
        {
            using var response = await httpFactory.CreateClient("lantern-api").SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? (await response.Content.ReadFromJsonAsync<Receipt>(ct), (int)response.StatusCode)
                : (null, (int)response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, 503);
        }
    }
}
