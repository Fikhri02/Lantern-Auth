namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// Cookies per origin, sent back regardless of the Secure flag. Keycloak marks every cookie Secure even
/// over http; browsers treat http://localhost as a secure context and send them, CookieContainer doesn't.
/// </summary>
public sealed class LocalhostCookieJar(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly Dictionary<string, Dictionary<string, string>> _byOrigin = new();

    public string? Get(string origin, string name) =>
        _byOrigin.TryGetValue(origin, out var cookies) && cookies.TryGetValue(name, out var value) ? value : null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var origin = request.RequestUri!.GetLeftPart(UriPartial.Authority);
        if (_byOrigin.TryGetValue(origin, out var cookies) && cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}")));

        var response = await base.SendAsync(request, ct);
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies)) return response;

        if (!_byOrigin.TryGetValue(origin, out cookies)) _byOrigin[origin] = cookies = new Dictionary<string, string>();
        foreach (var header in setCookies)
        {
            var parts = header.Split(';', StringSplitOptions.TrimEntries);
            var nameValue = parts[0].Split('=', 2);
            var expired = parts.Any(p => p.Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase)) ||
                          parts.Any(p => p.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase) &&
                                         DateTimeOffset.TryParse(p[8..], out var at) && at < DateTimeOffset.UtcNow);
            if (expired || nameValue.Length < 2 || nameValue[1].Length == 0) cookies.Remove(nameValue[0]);
            else cookies[nameValue[0]] = nameValue[1];
        }
        return response;
    }
}
