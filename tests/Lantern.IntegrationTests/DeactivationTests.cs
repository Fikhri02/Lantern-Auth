using System.Net;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class DeactivationTests(KeycloakFixture kc)
{
    [Fact]
    public async Task Disabled_user_is_rejected_on_next_call_when_cache_is_off()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        var client = kc.Api.ClientWithToken(await kc.GetFreshUserTokenAsync(username));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me")).StatusCode);

        await kc.SetUserEnabledAsync(id, false);
        await kc.LogoutUserAsync(id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task With_cache_window_a_disabled_user_keeps_access_until_it_expires()
    {
        await using var cachedApi = new ApiFactory(kc, introspectionCacheSeconds: 15);
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        var client = cachedApi.ClientWithToken(await kc.GetFreshUserTokenAsync(username));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me")).StatusCode);

        await kc.SetUserEnabledAsync(id, false);

        // Documents the trade-off in spec §5.4: up to IntrospectionCacheSeconds of stale access.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me")).StatusCode);
    }
}
