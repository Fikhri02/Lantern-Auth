using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;

namespace Lantern.Auth;

/// <summary>
/// Server-side sessions (spec §5.1): the browser cookie holds only a key; the ticket, with its tokens, stays here.
/// Indexed by Keycloak's sid so back-channel logout can remove every session of a Keycloak session (spec §5.2).
/// In memory, so restarting the app signs its users out.
/// </summary>
public sealed class ServerSessionStore : ITicketStore
{
    public const string SessionClaim = "lantern_session";

    private readonly ConcurrentDictionary<string, AuthenticationTicket> _tickets = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _keysBySid = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        if (ticket.Principal.Identity is ClaimsIdentity identity)
        {
            foreach (var old in identity.FindAll(SessionClaim).ToList()) identity.RemoveClaim(old);
            identity.AddClaim(new Claim(SessionClaim, key));
        }
        Put(key, ticket);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        Put(key, ticket);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        Task.FromResult(_tickets.TryGetValue(key, out var ticket) ? ticket : null);

    public Task RemoveAsync(string key)
    {
        if (_tickets.TryRemove(key, out var ticket) && Sid(ticket) is { } sid && _keysBySid.TryGetValue(sid, out var keys))
            keys.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <returns>How many sessions were removed.</returns>
    public async Task<int> RemoveBySidAsync(string sid)
    {
        if (!_keysBySid.TryRemove(sid, out var keys)) return 0;
        var removed = 0;
        foreach (var key in keys.Keys)
        {
            if (_tickets.ContainsKey(key)) removed++;
            await RemoveAsync(key);
        }
        return removed;
    }

    /// <summary>True while the principal's server session still exists (used by revalidation).</summary>
    public bool IsAlive(ClaimsPrincipal principal) =>
        principal.FindFirstValue(SessionClaim) is { } key && _tickets.ContainsKey(key);

    private void Put(string key, AuthenticationTicket ticket)
    {
        _tickets[key] = ticket;
        if (Sid(ticket) is { } sid) _keysBySid.GetOrAdd(sid, _ => new ConcurrentDictionary<string, byte>())[key] = 0;
    }

    private static string? Sid(AuthenticationTicket ticket) => ticket.Principal.FindFirstValue("sid");
}
