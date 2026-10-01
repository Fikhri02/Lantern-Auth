using System.Net;
using Lantern.Api.Auth;
using Lantern.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests;

/// <summary>Focus item 2 at the API level: when Keycloak can't confirm a token, the API refuses it.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class IntrospectionFailureTests(KeycloakFixture kc)
{
    private sealed class FailingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond());
    }

    private async Task<HttpStatusCode> MeStatusWhenIntrospectionAsync(Func<HttpResponseMessage> respond)
    {
        await using var api = new ApiFactory(kc, introspectionCacheSeconds: 0, services =>
            services.AddHttpClient<TokenIntrospector>().ConfigurePrimaryHttpMessageHandler(() => new FailingHandler(respond)));
        var client = api.ClientWithToken(await kc.GetUserTokenAsync("chloe.staff"));
        return (await client.GetAsync("/me")).StatusCode;
    }

    [Fact]
    public async Task Unreachable_keycloak_means_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            await MeStatusWhenIntrospectionAsync(() => throw new HttpRequestException("connection refused")));

    [Fact]
    public async Task Keycloak_error_means_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            await MeStatusWhenIntrospectionAsync(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
}
