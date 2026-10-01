using System.Security.Claims;
using Lantern.Auth;
using Microsoft.AspNetCore.Authentication;

namespace Lantern.IntegrationTests;

public sealed class ServerSessionStoreTests
{
    private static AuthenticationTicket Ticket(string sid, string user = "chloe.staff") =>
        new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", sid), new Claim("preferred_username", user)], "test")),
            new AuthenticationProperties(), "cookie");

    [Fact]
    public async Task Stored_ticket_carries_its_session_key_and_reads_back()
    {
        var store = new ServerSessionStore();

        var key = await store.StoreAsync(Ticket("sid-1"));
        var ticket = await store.RetrieveAsync(key);

        Assert.Equal(key, ticket!.Principal.FindFirstValue(ServerSessionStore.SessionClaim));
        Assert.Equal("chloe.staff", ticket.Principal.FindFirstValue("preferred_username"));
    }

    [Fact]
    public async Task Removing_by_sid_ends_every_session_of_that_keycloak_session()
    {
        var store = new ServerSessionStore();
        var a = await store.StoreAsync(Ticket("sid-1"));
        var b = await store.StoreAsync(Ticket("sid-1"));
        var other = await store.StoreAsync(Ticket("sid-2"));

        var removed = await store.RemoveBySidAsync("sid-1");

        Assert.Equal(2, removed);
        Assert.Null(await store.RetrieveAsync(a));
        Assert.Null(await store.RetrieveAsync(b));
        Assert.NotNull(await store.RetrieveAsync(other));
    }

    [Fact]
    public async Task Renewed_ticket_is_still_found_by_sid()
    {
        var store = new ServerSessionStore();
        var key = await store.StoreAsync(Ticket("sid-1"));
        var renewed = (await store.RetrieveAsync(key))!;

        await store.RenewAsync(key, renewed);
        await store.RemoveBySidAsync("sid-1");

        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task Principal_is_alive_only_while_its_session_exists()
    {
        var store = new ServerSessionStore();
        var key = await store.StoreAsync(Ticket("sid-1"));
        var principal = (await store.RetrieveAsync(key))!.Principal;

        Assert.True(store.IsAlive(principal));
        await store.RemoveAsync(key);
        Assert.False(store.IsAlive(principal));
        Assert.False(store.IsAlive(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task Unknown_sid_removes_nothing()
    {
        var store = new ServerSessionStore();
        await store.StoreAsync(Ticket("sid-1"));

        Assert.Equal(0, await store.RemoveBySidAsync("nope"));
    }
}
