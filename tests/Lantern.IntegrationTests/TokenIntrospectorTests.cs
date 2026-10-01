using System.Net;
using System.Text;
using Lantern.Api.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lantern.IntegrationTests;

public sealed class TokenIntrospectorTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static JsonWebToken Token(string jti) => new(new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Claims = new Dictionary<string, object> { ["jti"] = jti },
        Expires = DateTime.UtcNow.AddMinutes(5)
    }));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static TokenIntrospector Create(StubHandler handler, int cacheSeconds) => new(
        new HttpClient(handler),
        new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new KeycloakOptions { BaseUrl = "http://kc.test", ApiClientSecret = "s", IntrospectionCacheSeconds = cacheSeconds }),
        NullLogger<TokenIntrospector>.Instance);

    [Fact]
    public async Task Active_token_is_rechecked_every_time_when_cache_is_off()
    {
        var handler = new StubHandler(_ => Json("""{"active":true}"""));
        var sut = Create(handler, cacheSeconds: 0);
        var token = Token("a1");

        Assert.Equal(IntrospectionResult.Active, await sut.CheckAsync(token, default));
        Assert.Equal(IntrospectionResult.Active, await sut.CheckAsync(token, default));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Active_token_is_cached_within_window()
    {
        var handler = new StubHandler(_ => Json("""{"active":true}"""));
        var sut = Create(handler, cacheSeconds: 15);
        var token = Token("a2");

        await sut.CheckAsync(token, default);
        await sut.CheckAsync(token, default);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Inactive_token_is_cached_even_when_cache_is_off()
    {
        var handler = new StubHandler(_ => Json("""{"active":false}"""));
        var sut = Create(handler, cacheSeconds: 0);
        var token = Token("a3");

        Assert.Equal(IntrospectionResult.Inactive, await sut.CheckAsync(token, default));
        Assert.Equal(IntrospectionResult.Inactive, await sut.CheckAsync(token, default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Unreachable_keycloak_is_unavailable_not_an_exception()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = Create(handler, cacheSeconds: 15);

        Assert.Equal(IntrospectionResult.Unavailable, await sut.CheckAsync(Token("a4"), default));
    }

    [Fact]
    public async Task Keycloak_error_status_is_unavailable_and_not_cached()
    {
        var handler = new StubHandler(_ => Json("{}", HttpStatusCode.InternalServerError));
        var sut = Create(handler, cacheSeconds: 15);
        var token = Token("a5");

        Assert.Equal(IntrospectionResult.Unavailable, await sut.CheckAsync(token, default));
        Assert.Equal(IntrospectionResult.Unavailable, await sut.CheckAsync(token, default));
        Assert.Equal(2, handler.Calls);
    }
}
