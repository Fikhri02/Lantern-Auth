using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Lantern.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Lantern.IntegrationTests;

/// <summary>Refresh behaviour without Keycloak: what's stored, and when a session ends.</summary>
public sealed class AccessTokenProviderTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly ServerSessionStore _store = new();

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private AccessTokenProvider Provider(HttpMessageHandler handler) => new(_store, new Factory(handler),
        Options.Create(new LanternBffOptions { KeycloakBaseUrl = "http://kc.test", ClientId = "backoffice", ClientSecret = "s" }), _time);

    private async Task<(string Key, ClaimsPrincipal User)> SessionAsync(DateTimeOffset expiresAt)
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = "old-access" },
            new AuthenticationToken { Name = "refresh_token", Value = "old-refresh" },
            new AuthenticationToken { Name = "expires_at", Value = expiresAt.ToString("o", CultureInfo.InvariantCulture) }
        ]);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", "sid-1")], "test")), properties, "cookie");
        var key = await _store.StoreAsync(ticket);
        return (key, (await _store.RetrieveAsync(key))!.Principal);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Fresh_token_is_used_without_calling_keycloak()
    {
        var handler = new Handler(() => throw new InvalidOperationException("should not be called"));
        var (_, user) = await SessionAsync(_time.GetUtcNow().AddMinutes(4));

        var (outcome, token) = await Provider(handler).GetAsync(user, default);

        Assert.Equal(TokenOutcome.Ok, outcome);
        Assert.Equal("old-access", token);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Expired_token_is_refreshed_and_the_new_tokens_are_stored()
    {
        var handler = new Handler(() => Json(HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":300}"""));
        var (key, user) = await SessionAsync(_time.GetUtcNow().AddMinutes(-1));

        var (outcome, token) = await Provider(handler).GetAsync(user, default);

        Assert.Equal(TokenOutcome.Ok, outcome);
        Assert.Equal("new-access", token);
        var stored = (await _store.RetrieveAsync(key))!.Properties;
        Assert.Equal("new-access", stored.GetTokenValue("access_token"));
        Assert.Equal("new-refresh", stored.GetTokenValue("refresh_token"));
        Assert.True(DateTimeOffset.Parse(stored.GetTokenValue("expires_at")!, CultureInfo.InvariantCulture) > _time.GetUtcNow());
    }

    [Fact]
    public async Task Rejected_refresh_ends_the_session()
    {
        var handler = new Handler(() => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var (_, user) = await SessionAsync(_time.GetUtcNow().AddMinutes(-1));

        var (outcome, token) = await Provider(handler).GetAsync(user, default);

        Assert.Equal(TokenOutcome.SessionEnded, outcome);
        Assert.Null(token);
        Assert.False(_store.IsAlive(user));
    }

    [Fact]
    public async Task Unreachable_keycloak_keeps_the_session()
    {
        var handler = new Handler(() => throw new HttpRequestException("connection refused"));
        var (_, user) = await SessionAsync(_time.GetUtcNow().AddMinutes(-1));

        var (outcome, _) = await Provider(handler).GetAsync(user, default);

        Assert.Equal(TokenOutcome.Unavailable, outcome);
        Assert.True(_store.IsAlive(user));
    }

    [Fact]
    public async Task Keycloak_server_error_keeps_the_session()
    {
        var handler = new Handler(() => Json(HttpStatusCode.InternalServerError, """{"error":"unknown_error"}"""));
        var (_, user) = await SessionAsync(_time.GetUtcNow().AddMinutes(-1));

        Assert.Equal(TokenOutcome.Unavailable, (await Provider(handler).GetAsync(user, default)).Outcome);
        Assert.True(_store.IsAlive(user));
    }
}
