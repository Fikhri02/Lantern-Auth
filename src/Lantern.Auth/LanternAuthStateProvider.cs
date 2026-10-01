using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Logging;

namespace Lantern.Auth;

/// <summary>
/// Re-checks the server session every 30 s, so a tab left open notices a back-channel logout without a
/// page load (spec §5.2).
/// </summary>
public sealed class LanternAuthStateProvider(ILoggerFactory loggerFactory, ServerSessionStore sessions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct) =>
        Task.FromResult(sessions.IsAlive(state.User));
}
