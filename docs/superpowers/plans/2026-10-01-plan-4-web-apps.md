# Lantern Auth — Plan 4: Web Apps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Back Office and Outlet Admin as server-side Blazor apps sharing one sign-in library. They cover:
- single sign-on and single logout across both apps, with back-channel logout
- Keycloak refusing people who don't belong in an app
- MFA for HQ admins only
- a branded login page per app
- the pages that show role-based access: staff, promotions, outlet stock and roster, and the Tills page

**Architecture:**
- **Shared library.** `Lantern.Auth`, a class library, gives both apps the backend-for-frontend pattern:
  - cookie and OIDC (code + PKCE) authentication, with tokens kept server-side in an in-memory `ServerSessionStore` (an `ITicketStore` indexed by Keycloak `sid`)
  - `/bff/login`, `/bff/logout?sid=…`, and `/bff/backchannel-logout`
  - an `AccessTokenProvider` that refreshes tokens
  - a `LanternApi` client
  - a revalidating authentication-state provider
- **Realm.** It gains two browser flows, `browser-hq` and `browser-outlet`. Each is identify (cookie, or password plus conditional OTP for `hq-admin`), then a deny-unless-role step. It also gains three login themes and back-channel logout URLs.
- **Tests.**
  - Each app runs in memory, and a test browser routes `localhost:5200` and `localhost:5300` to them.
  - A small relay on the host receives Keycloak's back-channel logout calls through `host.docker.internal` and forwards them to the in-memory apps.
  - A test TOTP generator enrols and uses MFA.

**Tech Stack:** .NET 10 Blazor Web App (Interactive Server), `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.12, Keycloak 26.7.5 (`keycloak.v2` login theme as parent), xUnit, and Testcontainers, as in Plans 1–3.

**Spec:** `docs/superpowers/specs/2026-10-01-lantern-auth-design.md`, §4.5 (flow overrides, themes), §5.1, §5.2, §5.5 (UI side), §5.6, §5.7, §7 (web rows), §12.4. Roadmap: `docs/superpowers/plans/2026-10-01-roadmap.md`.

## Global Constraints

- Everything in Plans 1–3's Global Constraints still holds: versions, fictional setting, demo password `Lantern!2026`, conventional commits with **no trailers**, repo-local `user.email` `irfanfikhri@gmail.com`, and `dotnet` via `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`.
- Branch `irfan/web-apps`, created from `irfan/design-spec`.
- **Origins:**
  - Back Office: `http://localhost:5200`, client `backoffice`, secret `backoffice-dev-secret`, cookie `lantern.bo`.
  - Outlet Admin: `http://localhost:5300`, client `outlet-admin`, secret `outlet-admin-dev-secret`, cookie `lantern.oa`.
- **OIDC:** code flow with PKCE, response mode `query`, scope `openid profile email`. `SaveTokens` on, `MapInboundClaims` off. Name claim `preferred_username`, role claim `roles`. Callback `/signin-oidc`, signed-out callback `/signout-callback-oidc`.
- **Server-side sessions** are in memory, so restarting an app signs its users out (a documented demo limit, spec §5.2).
- **Back-channel logout:** Keycloak POSTs `logout_token` to `/bff/backchannel-logout`. It's validated for signature (realm keys from discovery), `iss`, `aud` (= client id), and the `events` claim. A `sid` must be present and a `nonce` absent. Every session with that `sid` is removed.
- **Revalidation:** `LanternAuthStateProvider` re-checks the server session every **30 s** (spec §5.2).
- **Deny pages** (Keycloak, themed):
  - "Your account doesn't have access to Back Office."
  - "Your account doesn't have access to Outlet Admin."
- **Login themes:** `lantern-hq` (backoffice), `lantern-outlet` (outlet-admin) and `lantern-till` (till). Each extends `keycloak.v2` and adds its own `kcHtmlClass` marker and CSS.
- **`Program` is namespaced** in both apps (`Lantern.BackOffice.Program`, `Lantern.OutletAdmin.Program`), as in Plan 3.
- **Configuration is lazy** (`IOptions<LanternBffOptions>`, section `"Lantern"`). `WebApplicationFactory` applies test settings only after `Program` registers services.
- **Browser tests use temporary users for anything an HQ admin does.** Enrolling MFA on a seeded admin would break the API tests, which get admin tokens by password grant.

**Plan decisions (no spec change):**
1. **Interactive actions aren't driven in this plan's tests.** Buttons in Interactive Server components (create staff, deactivate, release a till, the "try anyway" 403 demo) need a real browser circuit, so Plan 5 drives them with Playwright. This plan tests what HTTP can reach:
   - prerendered pages per role
   - SSO, single logout and deny pages
   - MFA enrolment and challenge
   - themes
   - token refresh
   The API behind every action is already tested in Plans 1–2.
2. **Outlet Admin's outlet:** the outlet comes from the token's `outlet_id`. An `hq-admin`, who has no outlet, picks one with `?outlet=BGS|KLC|PJY` (default `BGS`).
3. **Sign-out link:** `GET /bff/logout?sid=<session id>`. Requiring the user's own `sid` makes the link useless to a cross-site attacker, the usual BFF guard.
4. **Purchase-order approval** stays API-only in this plan, with no page; the spec has no screen for it.

## Review Focus

1. **A user already signed in to Outlet Admin opens Back Office without an HQ role.** Keycloak's cookie SSO must not skip the role check: they get the Back Office deny page. Test in Task 3.
2. **A logout token that isn't from Keycloak, or is meant for the other app,** is refused (400) and removes nothing. Test in Task 4.
3. **The sign-out link is opened with someone else's or a missing `sid`.** It's refused (400) and nobody is signed out. Test in Task 2.
4. **An HQ admin enrols MFA, then signs in again within the same 30-second window.** The second code must be a fresh one, Keycloak refuses a reused code, and the user gets in. Test helper and test in Task 3.
5. **An access token expires while the user has the app open** (more than 5 minutes later). The next page load refreshes it silently and still shows data. If the refresh fails (session ended), the user is sent to sign in. Tests in Task 5.

---

## File Structure

```
src/Lantern.Auth/                               # class library shared by both apps
  Lantern.Auth.csproj
  LanternBffOptions.cs                          # config shape
  ServerSessionStore.cs                         # ITicketStore + sid index + session claim
  LanternBffSetup.cs                            # AddLanternBff: cookie, OIDC, store, providers, http clients
  BffEndpoints.cs                               # /bff/login, /bff/logout, /bff/backchannel-logout
  LogoutTokenValidator.cs                       # back-channel logout token checks
  LanternAuthStateProvider.cs                   # revalidating auth state (30 s)
  AccessTokenProvider.cs                        # access token from the session, refreshed when near expiry
  LanternApi.cs                                 # GET/POST to the API with the user's token
  LanternRoles.cs                               # role names + helpers for the UI
src/Lantern.BackOffice/                         # from the empty Blazor Server template
  Program.cs  appsettings.json  Properties/launchSettings.json  Dockerfile  Lantern.BackOffice.csproj
  Components/Routes.razor  Components/_Imports.razor  Components/Layout/MainLayout.razor
  Components/RedirectToLogin.razor
  Components/Pages/Home.razor  Staff.razor  Promotions.razor  AccessDenied.razor
src/Lantern.OutletAdmin/                        # same shape
  Program.cs  appsettings.json  Properties/launchSettings.json  Dockerfile  Lantern.OutletAdmin.csproj
  Components/Routes.razor  Components/_Imports.razor  Components/Layout/MainLayout.razor
  Components/RedirectToLogin.razor
  Components/Pages/Home.razor  Tills.razor  AccessDenied.razor
keycloak/
  Dockerfile                                    # + COPY themes
  realm/lantern-realm.json                      # flows, overrides, backchannel URLs, login themes
  themes/lantern-hq/login/…  themes/lantern-outlet/login/…  themes/lantern-till/login/…
tests/Lantern.IntegrationTests/
  Infrastructure/BackchannelRelay.cs            # host relay for Keycloak → in-memory apps
  Infrastructure/Totp.cs                        # RFC 6238 codes for MFA tests
  Infrastructure/BffAppFactory.cs               # WebApplicationFactory for either app
  Infrastructure/AppBrowser.cs                  # multi-origin browser that can sign in through Keycloak
  Infrastructure/KeycloakFixture.cs             # + relay, backchannel URLs in the test realm, extra host
  ServerSessionStoreTests.cs  BackOfficeSignInTests.cs  SsoAndAccessTests.cs  SingleLogoutTests.cs
  BackOfficePageTests.cs  OutletAdminPageTests.cs  LoginThemeTests.cs
docker-compose.yml  scripts/smoke.sh  README.md
```

---

### Task 1: Server-side session store

**Files:**
- Create: `src/Lantern.Auth/Lantern.Auth.csproj`, `src/Lantern.Auth/ServerSessionStore.cs`
- Modify: `Lantern.slnx`, `tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`
- Test: `tests/Lantern.IntegrationTests/ServerSessionStoreTests.cs`

**Interfaces:**
- Produces:
  - `ServerSessionStore : ITicketStore`.
  - `const string SessionClaim = "lantern_session"`.
  - `StoreAsync`, `RenewAsync`, `RetrieveAsync`, `RemoveAsync` (the `ITicketStore` overloads without a cancellation token).
  - `RemoveBySidAsync(string sid)` → `int` removed.
  - `IsAlive(ClaimsPrincipal)` → `bool`.
  - `StoreAsync` puts a `lantern_session` claim holding its key on the ticket's identity, and indexes the key by the principal's `sid` claim.

- [ ] **Step 1: Create the branch and the library**

```bash
cd ~/projects/Personal/DotNet/lantern-auth
git checkout -b irfan/web-apps irfan/design-spec
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH
mkdir -p src/Lantern.Auth
```

`src/Lantern.Auth/Lantern.Auth.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.12" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln add src/Lantern.Auth/Lantern.Auth.csproj
```

`tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`: add to the project-reference `ItemGroup`:
```xml
    <ProjectReference Include="..\..\src\Lantern.Auth\Lantern.Auth.csproj" />
```

- [ ] **Step 2: Write the failing tests**

`tests/Lantern.IntegrationTests/ServerSessionStoreTests.cs`:
```csharp
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
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~ServerSessionStoreTests`
Expected: FAIL, with a compilation error: `ServerSessionStore` is not found.

- [ ] **Step 4: Implement**

`src/Lantern.Auth/ServerSessionStore.cs`:
```csharp
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
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~ServerSessionStoreTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add Lantern.slnx src/Lantern.Auth tests
git commit -m "feat: server-side session store indexed by keycloak sid"
```

---

### Task 2: The BFF sign-in library and Back Office sign-in

**Files:**
- Create: `src/Lantern.Auth/LanternBffOptions.cs`, `LanternBffSetup.cs`, `BffEndpoints.cs`, `LanternAuthStateProvider.cs`, `LanternRoles.cs`
- Create: `src/Lantern.BackOffice/` (from the template); replace `Program.cs`, `Lantern.BackOffice.csproj`, `appsettings.json`, `Properties/launchSettings.json`, `Components/Routes.razor`, `Components/_Imports.razor`, `Components/Layout/MainLayout.razor`, `Components/Pages/Home.razor`; add `Components/RedirectToLogin.razor`, `Components/Pages/AccessDenied.razor`
- Create: `tests/Lantern.IntegrationTests/Infrastructure/BffAppFactory.cs`, `Infrastructure/AppBrowser.cs`, `Infrastructure/Totp.cs`
- Modify: `tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`, `Lantern.slnx`
- Test: `tests/Lantern.IntegrationTests/BackOfficeSignInTests.cs`

**Interfaces:**
- Consumes: `ServerSessionStore` (Task 1), the realm's `backoffice` client (Plan 1), and fixture helpers `CreateTempUserAsync`, `UserSessionCountAsync`, `kc.Api`.
- Produces:
  - `LanternBffOptions` with `KeycloakBaseUrl`, `Realm`, `Issuer`, `ClientId`, `ClientSecret`, `ApiBaseUrl`, `CookieName`, plus computed `RealmUrl`.
  - `IServiceCollection.AddLanternBff(IConfiguration)` and `IEndpointRouteBuilder.MapLanternBff()`.
  - Endpoints: `GET /bff/login?returnUrl=/…` and `GET /bff/logout?sid=…`. Task 4 adds the back-channel endpoint.
  - `LanternRoles`: `Known`, `Of(ClaimsPrincipal)` → `string[]` sorted, and `Sid(ClaimsPrincipal)`.
  - `LanternAuthStateProvider` (revalidates every 30 s).
  - Test infrastructure:
    - `BffAppFactory<TProgram>(KeycloakFixture kc, string clientId, string clientSecret, string cookieName, Action<IServiceCollection>? configureServices = null)` with `SessionStore`
    - `AppBrowser(params (string Origin, HttpMessageHandler Handler)[] apps)` with:
      - `GetAsync(url)` → `BrowserPage`
      - `SignInAsync(url, username, password = DemoPassword, Totp? totp = null)` → `BrowserPage`, handling Keycloak's password, TOTP-setup and OTP pages
      - `IsKeycloakLogin(BrowserPage)`
    - `Totp` with `Secret` and `NextCode()`
    - constants `AppBrowser.BackOfficeOrigin` (`http://localhost:5200`) and `AppBrowser.OutletAdminOrigin` (`http://localhost:5300`)

- [ ] **Step 1: Create the Back Office app from the template**

```bash
dotnet new blazor -n Lantern.BackOffice -o src/Lantern.BackOffice --interactivity Server --empty
rm src/Lantern.BackOffice/appsettings.Development.json
dotnet sln add src/Lantern.BackOffice/Lantern.BackOffice.csproj
```

`tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`: add:
```xml
    <ProjectReference Include="..\..\src\Lantern.BackOffice\Lantern.BackOffice.csproj" />
```

- [ ] **Step 2: Write the test infrastructure and the failing tests**

`tests/Lantern.IntegrationTests/Infrastructure/Totp.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// An authenticator app for tests: RFC 6238 TOTP with Keycloak's defaults (HMAC-SHA1, 6 digits, 30 s).
/// Keycloak's key is the UTF-8 bytes of the secret in its setup form. Never hands out the same time step
/// twice, because Keycloak refuses a reused code.
/// </summary>
public sealed class Totp
{
    private long _lastStep = -1;

    public string? Secret { get; set; }

    public string NextCode()
    {
        if (Secret is null) throw new InvalidOperationException("No TOTP secret yet; enrol first.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var step = Math.Max(now, _lastStep + 1);
        if (step > now + 1) // beyond Keycloak's one-step look-ahead: wait for the next window
        {
            Thread.Sleep(TimeSpan.FromSeconds(30 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 + 1));
            return NextCode();
        }
        _lastStep = step;
        return Code(step);
    }

    private string Code(long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(Encoding.UTF8.GetBytes(Secret!), counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }
}
```

`tests/Lantern.IntegrationTests/Infrastructure/AppBrowser.cs`:
```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// A browser across several in-memory apps (by origin) and the real Keycloak. Follows redirects, keeps
/// cookies per origin, and can complete Keycloak's password, TOTP-setup and OTP pages.
/// </summary>
public sealed partial class AppBrowser : IDisposable
{
    public const string BackOfficeOrigin = "http://localhost:5200";
    public const string OutletAdminOrigin = "http://localhost:5300";

    private readonly LocalhostCookieJar _jar;
    private readonly HttpClient _http;

    public AppBrowser(params (string Origin, HttpMessageHandler Handler)[] apps)
    {
        _jar = new LocalhostCookieJar(new Router(apps.ToDictionary(a => a.Origin, a => new HttpMessageInvoker(a.Handler)),
            new HttpMessageInvoker(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })));
        _http = new HttpClient(_jar);
    }

    public Task<BrowserPage> GetAsync(string url) => FollowAsync(new HttpRequestMessage(HttpMethod.Get, url));

    public static bool IsKeycloakLogin(BrowserPage page) => FormAction(page.Html, "kc-form-login") is not null;

    /// <summary>Opens <paramref name="url"/>; if Keycloak asks, signs in (enrolling or answering TOTP when it asks).</summary>
    public async Task<BrowserPage> SignInAsync(string url, string username, string password = KeycloakFixture.DemoPassword, Totp? totp = null)
    {
        var page = await GetAsync(url);
        if (!IsKeycloakLogin(page)) return page;

        page = await PostAsync(FormAction(page.Html, "kc-form-login")!, new()
        {
            ["username"] = username, ["password"] = password, ["credentialId"] = ""
        });

        if (FormAction(page.Html, "kc-totp-settings-form") is { } setup)
        {
            totp ??= new Totp();
            totp.Secret = HiddenValue(page.Html, "totpSecret");
            page = await PostAsync(setup, new()
            {
                ["totp"] = totp.NextCode(), ["totpSecret"] = totp.Secret!, ["userLabel"] = "test phone"
            });
        }
        else if (FormAction(page.Html, "kc-otp-login-form") is { } otp)
        {
            if (totp?.Secret is null) throw new InvalidOperationException("Keycloak asked for a one-time code but the test has no TOTP secret.");
            page = await PostAsync(otp, new() { ["otp"] = totp.NextCode() });
        }
        return page;
    }

    public void Dispose() => _http.Dispose();

    private Task<BrowserPage> PostAsync(string action, Dictionary<string, string> form) =>
        FollowAsync(new HttpRequestMessage(HttpMethod.Post, action) { Content = new FormUrlEncodedContent(form) });

    private async Task<BrowserPage> FollowAsync(HttpRequestMessage request)
    {
        for (var hop = 0; hop < 25; hop++)
        {
            using var response = await _http.SendAsync(request);
            var url = request.RequestUri!;
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                request = new HttpRequestMessage(HttpMethod.Get, location.IsAbsoluteUri ? location : new Uri(url, location));
                continue;
            }
            return new BrowserPage(url, (int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        throw new InvalidOperationException("Too many redirects");
    }

    [GeneratedRegex("<form\\b[^>]*>")]
    private static partial Regex FormTag();

    [GeneratedRegex("\\baction=\"([^\"]+)\"")]
    private static partial Regex ActionAttribute();

    private static string? FormAction(string html, string formId)
    {
        var tag = FormTag().Matches(html).FirstOrDefault(m => m.Value.Contains($"id=\"{formId}\""));
        if (tag is null) return null;
        var action = ActionAttribute().Match(tag.Value);
        return action.Success ? WebUtility.HtmlDecode(action.Groups[1].Value) : null;
    }

    private static string HiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $"<input[^>]*name=\"{name}\"[^>]*value=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException($"No hidden field {name}");
    }

    private sealed class Router(Dictionary<string, HttpMessageInvoker> apps, HttpMessageInvoker network) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            apps.TryGetValue(request.RequestUri!.GetLeftPart(UriPartial.Authority), out var app)
                ? app.SendAsync(request, ct)
                : network.SendAsync(request, ct);
    }
}
```

`tests/Lantern.IntegrationTests/Infrastructure/BffAppFactory.cs`:
```csharp
using Lantern.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>Back Office or Outlet Admin in memory, pointed at the test Keycloak and the in-memory API.</summary>
public sealed class BffAppFactory<TProgram>(KeycloakFixture kc, string clientId, string clientSecret, string cookieName,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lantern:KeycloakBaseUrl"] = kc.BaseUrl,
            ["Lantern:Issuer"] = kc.Issuer,
            ["Lantern:ClientId"] = clientId,
            ["Lantern:ClientSecret"] = clientSecret,
            ["Lantern:CookieName"] = cookieName,
            ["Lantern:ApiBaseUrl"] = "http://lantern-api"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(LanternBffSetup.ApiClient).ConfigurePrimaryHttpMessageHandler(() => kc.Api.Server.CreateHandler());
            configureServices?.Invoke(services);
        });
    }

    public ServerSessionStore SessionStore => Services.GetRequiredService<ServerSessionStore>();

    /// <summary>A handler into the in-memory app, for <see cref="AppBrowser"/>. Task 4 also routes Keycloak's back-channel here.</summary>
    public HttpMessageHandler Handler() => Server.CreateHandler();
}

public static class BffApps
{
    public static BffAppFactory<Lantern.BackOffice.Program> BackOffice(KeycloakFixture kc, Action<IServiceCollection>? configure = null) =>
        new(kc, "backoffice", "backoffice-dev-secret", "lantern.bo", configure);
}
```

`tests/Lantern.IntegrationTests/BackOfficeSignInTests.cs`:
```csharp
using Lantern.IntegrationTests.Infrastructure;
using System.Text.RegularExpressions;

namespace Lantern.IntegrationTests;

/// <summary>Spec §5.1: server-side sign-in; §3 decision 3: sid-guarded sign-out.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class BackOfficeSignInTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private AppBrowser _browser = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc);
        _browser = new AppBrowser((AppBrowser.BackOfficeOrigin, _bo.Handler()));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Anonymous_visitor_is_sent_to_keycloak()
    {
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");
        Assert.True(AppBrowser.IsKeycloakLogin(page));
    }

    [Fact]
    public async Task Hq_staff_signs_in_and_sees_their_name_and_roles()
    {
        var page = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");

        Assert.Equal(AppBrowser.BackOfficeOrigin + "/", page.Url.ToString());
        Assert.Contains("Chloe Wong", page.Html);
        Assert.Contains("data-testid=\"role-hq-staff\"", page.Html);
        Assert.Contains("data-testid=\"sign-out\"", page.Html);
    }

    [Fact]
    public async Task Tokens_never_reach_the_browser()
    {
        var page = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");
        Assert.DoesNotContain("eyJ", page.Html); // no JWT anywhere in the page
    }

    [Fact]
    public async Task Sign_out_link_ends_the_keycloak_session()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        var home = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", username);
        var signOut = Regex.Match(home.Html, "href=\"(/bff/logout\\?sid=[^\"]+)\"").Groups[1].Value;

        await _browser.GetAsync(AppBrowser.BackOfficeOrigin + System.Net.WebUtility.HtmlDecode(signOut));

        Assert.Equal(0, await kc.UserSessionCountAsync(id));
        Assert.True(AppBrowser.IsKeycloakLogin(await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/")));
    }

    [Theory]
    [InlineData("/bff/logout")]
    [InlineData("/bff/logout?sid=someone-else")]
    public async Task Sign_out_without_your_own_sid_is_refused(string path)
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", username);

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + path);

        Assert.Equal(400, page.Status);
        Assert.Equal(1, await kc.UserSessionCountAsync(id));
    }

    public async Task DisposeAsync()
    {
        _browser.Dispose();
        await _bo.DisposeAsync();
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~BackOfficeSignInTests`
Expected: FAIL, with compilation errors: `Lantern.BackOffice.Program` is not accessible (the template has a top-level `Program`), and `LanternBffSetup` is not found.

- [ ] **Step 4: Implement the library**

`src/Lantern.Auth/LanternBffOptions.cs`:
```csharp
namespace Lantern.Auth;

public sealed class LanternBffOptions
{
    public const string Section = "Lantern";

    public string KeycloakBaseUrl { get; set; } = "http://localhost:8080";
    public string Realm { get; set; } = "lantern";
    /// <summary>Exact iss in tokens; differs from KeycloakBaseUrl inside Docker (spec §8).</summary>
    public string Issuer { get; set; } = "http://localhost:8080/realms/lantern";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "http://localhost:5100";
    /// <summary>Distinct per app: browsers don't separate cookies by port on localhost (spec §5.1).</summary>
    public string CookieName { get; set; } = "lantern.app";

    public string RealmUrl => $"{KeycloakBaseUrl.TrimEnd('/')}/realms/{Realm}";
}
```

`src/Lantern.Auth/LanternRoles.cs`:
```csharp
using System.Security.Claims;

namespace Lantern.Auth;

public static class LanternRoles
{
    public const string HqAdmin = "hq-admin";
    public const string HqStaff = "hq-staff";
    public const string Marketing = "marketing";
    public const string Procurement = "procurement";
    public const string Finance = "finance";
    public const string OutletManager = "outlet-manager";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>
    {
        HqAdmin, HqStaff, Marketing, Procurement, Finance, OutletManager, "outlet-device", "cashier"
    };

    public static string[] Of(ClaimsPrincipal user) =>
        user.FindAll("roles").Select(c => c.Value).Where(Known.Contains).Order().ToArray();

    public static string? Sid(ClaimsPrincipal user) => user.FindFirstValue("sid");
}
```

`src/Lantern.Auth/LanternAuthStateProvider.cs`:
```csharp
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
```

`src/Lantern.Auth/LanternBffSetup.cs`:
```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Lantern.Auth;

public static class LanternBffSetup
{
    public const string ApiClient = "lantern-api";
    public const string KeycloakClient = "keycloak";

    /// <summary>Spec §5.1: cookie + OIDC (code + PKCE), tokens kept server-side in <see cref="ServerSessionStore"/>.</summary>
    public static IServiceCollection AddLanternBff(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<LanternBffOptions>(config.GetSection(LanternBffOptions.Section));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServerSessionStore>();
        services.AddHttpClient(ApiClient, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(KeycloakClient, c => c.Timeout = TimeSpan.FromSeconds(10));

        services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect();

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<LanternBffOptions>, ServerSessionStore>((o, bff, sessions) =>
            {
                o.Cookie.Name = bff.Value.CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.SessionStore = sessions;
                o.AccessDeniedPath = "/access-denied";
            });

        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<LanternBffOptions>>((o, bffOptions) =>
            {
                var bff = bffOptions.Value;
                o.MetadataAddress = $"{bff.RealmUrl}/.well-known/openid-configuration";
                o.RequireHttpsMetadata = false;
                o.ClientId = bff.ClientId;
                o.ClientSecret = bff.ClientSecret;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.ResponseMode = OpenIdConnectResponseMode.Query;
                o.UsePkce = true;
                o.Scope.Clear();
                foreach (var scope in new[] { "openid", "profile", "email" }) o.Scope.Add(scope);
                o.SaveTokens = true;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.TokenValidationParameters.ValidIssuer = bff.Issuer;
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.TokenValidationParameters.RoleClaimType = "roles";
                o.CallbackPath = "/signin-oidc";
                o.SignedOutCallbackPath = "/signout-callback-oidc";
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.NonceCookie.SameSite = SameSiteMode.Lax;
                o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, LanternAuthStateProvider>();
        services.AddAuthorization();
        return services;
    }
}
```

`src/Lantern.Auth/BffEndpoints.cs`:
```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lantern.Auth;

public static class BffEndpoints
{
    public static IEndpointRouteBuilder MapLanternBff(this IEndpointRouteBuilder app)
    {
        app.MapGet("/bff/login", (string? returnUrl) =>
            Results.Challenge(new AuthenticationProperties { RedirectUri = LocalOnly(returnUrl) },
                [OpenIdConnectDefaults.AuthenticationScheme]));

        // The sid parameter must match the signed-in session, so a link on another site can't sign anyone out.
        app.MapGet("/bff/logout", (HttpContext http, string? sid) =>
        {
            var mine = LanternRoles.Sid(http.User);
            if (http.User.Identity?.IsAuthenticated != true || mine is null || sid != mine)
                return Results.BadRequest();
            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        });

        return app;
    }

    private static string LocalOnly(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";
}
```

- [ ] **Step 5: Implement the Back Office shell**

`src/Lantern.BackOffice/Lantern.BackOffice.csproj` (replace):
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Lantern.Auth\Lantern.Auth.csproj" />
  </ItemGroup>
</Project>
```

`src/Lantern.BackOffice/Program.cs` (replace):
```csharp
using Lantern.Auth;
using Lantern.BackOffice.Components;

namespace Lantern.BackOffice;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddLanternBff(builder.Configuration);

        var app = builder.Build();
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapLanternBff();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.Run();
    }
}
```

`src/Lantern.BackOffice/appsettings.json` (replace):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Lantern": {
    "KeycloakBaseUrl": "http://localhost:8080",
    "Realm": "lantern",
    "Issuer": "http://localhost:8080/realms/lantern",
    "ClientId": "backoffice",
    "ClientSecret": "backoffice-dev-secret",
    "ApiBaseUrl": "http://localhost:5100",
    "CookieName": "lantern.bo"
  }
}
```

`src/Lantern.BackOffice/Properties/launchSettings.json` (replace):
```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5200",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

`src/Lantern.BackOffice/Components/_Imports.razor`: append:
```razor
@using System.Security.Claims
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Lantern.Auth
```

`src/Lantern.BackOffice/Components/Routes.razor` (replace):
```razor
<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                @if (context.User.Identity?.IsAuthenticated == true)
                {
                    <p data-testid="not-allowed">You don't have access to this page.</p>
                }
                else
                {
                    <RedirectToLogin />
                }
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

`src/Lantern.BackOffice/Components/RedirectToLogin.razor`:
```razor
@inject NavigationManager Navigation

@code {
    protected override void OnInitialized() =>
        Navigation.NavigateTo($"/bff/login?returnUrl={Uri.EscapeDataString("/" + Navigation.ToBaseRelativePath(Navigation.Uri))}", forceLoad: true);
}
```

`src/Lantern.BackOffice/Components/Layout/MainLayout.razor` (replace):
```razor
@inherits LayoutComponentBase

<header class="app-bar">
    <strong>Lantern Back Office</strong>
    <AuthorizeView>
        <Authorized>
            <span class="who" data-testid="signed-in-as">@context.User.FindFirst("name")?.Value (@context.User.Identity?.Name)</span>
            <a href="/bff/logout?sid=@Uri.EscapeDataString(LanternRoles.Sid(context.User) ?? "")" data-testid="sign-out">Sign out</a>
        </Authorized>
    </AuthorizeView>
</header>

<main class="page">
    @Body
</main>
```

`src/Lantern.BackOffice/Components/Pages/Home.razor` (replace):
```razor
@page "/"
@attribute [Authorize]

<PageTitle>Lantern Back Office</PageTitle>

<h1>Back Office</h1>

<section data-testid="roles">
    <h2>Your roles</h2>
    <ul>
        @foreach (var role in roles)
        {
            <li data-testid="role-@role">@role</li>
        }
    </ul>
</section>

@code {
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    private string[] roles = [];

    protected override async Task OnInitializedAsync() => roles = LanternRoles.Of((await AuthState).User);
}
```

`src/Lantern.BackOffice/Components/Pages/AccessDenied.razor`:
```razor
@page "/access-denied"

<PageTitle>No access</PageTitle>

<h1>No access</h1>
<p data-testid="access-denied">Your account doesn't have access to this page.</p>
<p><a href="/">Back to the start page</a></p>
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~BackOfficeSignInTests`
Expected: PASS (6 tests). If sign-in lands on `/signin-oidc` with a 500, check that the id token issuer equals `Lantern:Issuer`. If the sign-out test still finds a Keycloak session, the `id_token` wasn't saved: `SaveTokens` must be true, and the session store must keep the ticket's properties.

Then run: `dotnet test`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add Lantern.slnx src/Lantern.Auth src/Lantern.BackOffice tests
git commit -m "feat: bff sign-in library and back office sign-in"
```

---

### Task 3: Outlet Admin, single sign-on, deny-unless-role flows and MFA for HQ admins

**Files:**
- Create: `src/Lantern.OutletAdmin/` (from the template, same replacements as Back Office), `Components/Pages/Home.razor`, `Components/Pages/AccessDenied.razor`
- Modify: `keycloak/realm/lantern-realm.json`, `tests/Lantern.IntegrationTests/Infrastructure/BffAppFactory.cs`, `tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`, `Lantern.slnx`
- Test: `tests/Lantern.IntegrationTests/SsoAndAccessTests.cs`

**Interfaces:**
- Consumes: `AddLanternBff`, `MapLanternBff`, `LanternRoles` (Task 2), `AppBrowser`, `Totp`, `BffAppFactory` (Task 2).
- Produces:
  - Realm flows:
    - `browser-hq` (id `7b2e4c61-9d3a-4f5e-8a1b-2c6d9e0f1a01`)
    - `browser-outlet` (id `7b2e4c61-9d3a-4f5e-8a1b-2c6d9e0f1a02`)
    - Each has an identify step (cookie, or password then OTP if `hq-admin`), then deny-unless-role.
  - Client overrides: `backoffice.browser` → `browser-hq`, `outlet-admin.browser` → `browser-outlet`.
  - `BffApps.OutletAdmin(kc, configure)`.

- [ ] **Step 1: Create the Outlet Admin app**

```bash
dotnet new blazor -n Lantern.OutletAdmin -o src/Lantern.OutletAdmin --interactivity Server --empty
rm src/Lantern.OutletAdmin/appsettings.Development.json
dotnet sln add src/Lantern.OutletAdmin/Lantern.OutletAdmin.csproj
```

Copy Back Office's shell files across with the app's names, client, cookie and port substituted:
```bash
python3 - <<'EOF2'
import pathlib
src, dst = pathlib.Path("src/Lantern.BackOffice"), pathlib.Path("src/Lantern.OutletAdmin")
for rel in ["Program.cs", "Lantern.BackOffice.csproj", "appsettings.json", "Properties/launchSettings.json",
            "Components/Routes.razor", "Components/_Imports.razor", "Components/RedirectToLogin.razor",
            "Components/Layout/MainLayout.razor", "Components/Pages/AccessDenied.razor"]:
    text = (src / rel).read_text()
    for a, b in [("Lantern.BackOffice", "Lantern.OutletAdmin"), ("Lantern Back Office", "Lantern Outlet Admin"),
                 ('"ClientId": "backoffice"', '"ClientId": "outlet-admin"'), ("backoffice-dev-secret", "outlet-admin-dev-secret"),
                 ("lantern.bo", "lantern.oa"), ("localhost:5200", "localhost:5300")]:
        text = text.replace(a, b)
    target = dst / rel.replace("Lantern.BackOffice.csproj", "Lantern.OutletAdmin.csproj")
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text)
EOF2
```

`src/Lantern.OutletAdmin/Components/Pages/Home.razor` (replace; Task 6 adds stock and roster):
```razor
@page "/"
@attribute [Authorize]

<PageTitle>Lantern Outlet Admin</PageTitle>

<h1>Outlet Admin</h1>

<section data-testid="roles">
    <ul>
        @foreach (var role in roles)
        {
            <li data-testid="role-@role">@role</li>
        }
    </ul>
</section>

@code {
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    private string[] roles = [];

    protected override async Task OnInitializedAsync() => roles = LanternRoles.Of((await AuthState).User);
}
```

`tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`: add:
```xml
    <ProjectReference Include="..\..\src\Lantern.OutletAdmin\Lantern.OutletAdmin.csproj" />
```

`tests/Lantern.IntegrationTests/Infrastructure/BffAppFactory.cs`: add to `BffApps`:
```csharp
    public static BffAppFactory<Lantern.OutletAdmin.Program> OutletAdmin(KeycloakFixture kc, Action<IServiceCollection>? configure = null) =>
        new(kc, "outlet-admin", "outlet-admin-dev-secret", "lantern.oa", configure);
```

- [ ] **Step 2: Write the failing tests**

`tests/Lantern.IntegrationTests/SsoAndAccessTests.cs`:
```csharp
using System.Net;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Scenarios A (SSO), G (MFA for hq-admin) and the deny-unless-role steps (spec §4.5, §5.6).</summary>
[Collection(KeycloakCollection.Name)]
public sealed class SsoAndAccessTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private BffAppFactory<Lantern.OutletAdmin.Program> _oa = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc);
        _oa = BffApps.OutletAdmin(kc);
        return Task.CompletedTask;
    }

    private AppBrowser NewBrowser() => new(
        (AppBrowser.BackOfficeOrigin, _bo.Handler()),
        (AppBrowser.OutletAdminOrigin, _oa.Handler()));

    [Fact]
    public async Task Hq_admin_enrols_mfa_once_then_opens_outlet_admin_without_signing_in_again()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        using var browser = NewBrowser();
        var totp = new Totp();

        var backOffice = await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: totp);
        var outletAdmin = await browser.GetAsync(AppBrowser.OutletAdminOrigin + "/");

        Assert.NotNull(totp.Secret); // Keycloak made the admin set up an authenticator app
        Assert.Equal(AppBrowser.BackOfficeOrigin + "/", backOffice.Url.ToString());
        Assert.Equal(AppBrowser.OutletAdminOrigin + "/", outletAdmin.Url.ToString());
        Assert.Contains("data-testid=\"role-hq-admin\"", outletAdmin.Html);
    }

    [Fact]
    public async Task Hq_admin_is_asked_for_a_fresh_code_on_the_next_sign_in()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        var totp = new Totp();
        using (var first = NewBrowser()) await first.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: totp);

        using var second = NewBrowser();
        var page = await second.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: totp);

        Assert.Equal(AppBrowser.BackOfficeOrigin + "/", page.Url.ToString());
    }

    [Fact]
    public async Task Staff_without_admin_role_are_not_asked_for_mfa()
    {
        using var browser = NewBrowser();
        var page = await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");
        Assert.Equal(AppBrowser.BackOfficeOrigin + "/", page.Url.ToString()); // would throw if an OTP page appeared
    }

    [Fact]
    public async Task Outlet_manager_is_refused_by_back_office_at_keycloak()
    {
        using var browser = NewBrowser();
        var page = await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "mgr.bangsar");

        Assert.Contains("Your account doesn't have access to Back Office.", WebUtility.HtmlDecode(page.Html));
        Assert.NotEqual(AppBrowser.BackOfficeOrigin, page.Url.GetLeftPart(UriPartial.Authority));
    }

    [Fact]
    public async Task Outlet_manager_signs_in_to_outlet_admin()
    {
        using var browser = NewBrowser();
        var page = await browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");
        Assert.Contains("data-testid=\"role-outlet-manager\"", page.Html);
    }

    [Fact]
    public async Task Hq_staff_are_refused_by_outlet_admin()
    {
        using var browser = NewBrowser();
        var page = await browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "chloe.staff");
        Assert.Contains("Your account doesn't have access to Outlet Admin.", WebUtility.HtmlDecode(page.Html));
    }

    [Fact]
    public async Task Single_sign_on_from_outlet_admin_does_not_skip_back_offices_role_check()
    {
        using var browser = NewBrowser();
        await browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");

        var page = await browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");

        Assert.Contains("Your account doesn't have access to Back Office.", WebUtility.HtmlDecode(page.Html));
    }

    public async Task DisposeAsync()
    {
        await _bo.DisposeAsync();
        await _oa.DisposeAsync();
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~SsoAndAccessTests`
Expected: FAIL on behaviour. With the default browser flow:
- no MFA is set up, so `totp.Secret` stays null
- the manager gets into Back Office, and so does HQ staff into Outlet Admin
- SSO succeeds without a role check

`Outlet_manager_signs_in_to_outlet_admin` and `Staff_without_admin_role_are_not_asked_for_mfa` pass already; they are guards.

- [ ] **Step 4: Add the flows and bind them**

```bash
python3 - <<'EOF'
import json, pathlib
p = pathlib.Path("keycloak/realm/lantern-realm.json")
realm = json.loads(p.read_text())

realm["authenticatorConfig"].extend([
    {"alias": "otp-for-hq-admin", "config": {"condUserRole": "hq-admin", "negate": "false"}},
    {"alias": "hq-without-hq-staff", "config": {"condUserRole": "hq-staff", "negate": "true"}},
    {"alias": "hq-deny-message", "config": {"denyErrorMessage": "Your account doesn't have access to Back Office."}},
    {"alias": "outlet-without-manager", "config": {"condUserRole": "outlet-manager", "negate": "true"}},
    {"alias": "outlet-without-admin", "config": {"condUserRole": "hq-admin", "negate": "true"}},
    {"alias": "outlet-deny-message", "config": {"denyErrorMessage": "Your account doesn't have access to Outlet Admin."}},
])

def step(provider, priority, requirement="REQUIRED", config=None):
    s = {"authenticator": provider, "requirement": requirement, "priority": priority,
         "authenticatorFlow": False, "userSetupAllowed": False}
    if config: s["authenticatorConfig"] = config
    return s

def sub(alias, priority, requirement):
    return {"requirement": requirement, "priority": priority, "authenticatorFlow": True,
            "flowAlias": alias, "userSetupAllowed": False}

def flow(alias, description, executions, flow_id=None, top=False):
    f = {"alias": alias, "description": description, "providerId": "basic-flow",
         "topLevel": top, "builtIn": False, "authenticationExecutions": executions}
    if flow_id: f["id"] = flow_id
    return f

def browser_flow(prefix, flow_id, deny_steps, description):
    return [
        flow(prefix, description, [
            sub(f"{prefix} identify", 10, "REQUIRED"),
            sub(f"{prefix} deny", 20, "CONDITIONAL"),
        ], flow_id, top=True),
        flow(f"{prefix} identify", "Keycloak session cookie, or password then OTP for hq-admin", [
            step("auth-cookie", 10, "ALTERNATIVE"),
            sub(f"{prefix} forms", 20, "ALTERNATIVE"),
        ]),
        flow(f"{prefix} forms", "Password, then OTP when the user is an hq-admin", [
            step("auth-username-password-form", 10),
            sub(f"{prefix} otp", 20, "CONDITIONAL"),
        ]),
        flow(f"{prefix} otp", "OTP for hq-admin only (spec 5.6)", [
            step("conditional-user-role", 10, config="otp-for-hq-admin"),
            step("auth-otp-form", 20),
        ]),
        # Runs after identify, whether the user typed a password or came in on the SSO cookie.
        flow(f"{prefix} deny", "Refuse users without the app's roles", deny_steps),
    ]

realm["authenticationFlows"].extend(browser_flow("browser-hq", "7b2e4c61-9d3a-4f5e-8a1b-2c6d9e0f1a01", [
    step("conditional-user-role", 10, config="hq-without-hq-staff"),
    step("deny-access-authenticator", 20, config="hq-deny-message"),
], "Back Office sign-in: SSO or password (+OTP for hq-admin), then HQ roles only"))

realm["authenticationFlows"].extend(browser_flow("browser-outlet", "7b2e4c61-9d3a-4f5e-8a1b-2c6d9e0f1a02", [
    step("conditional-user-role", 10, config="outlet-without-manager"),
    step("conditional-user-role", 11, config="outlet-without-admin"),
    step("deny-access-authenticator", 20, config="outlet-deny-message"),
], "Outlet Admin sign-in: SSO or password (+OTP for hq-admin), then outlet-manager or hq-admin only"))

clients = {c["clientId"]: c for c in realm["clients"]}
clients["backoffice"].setdefault("authenticationFlowBindingOverrides", {})["browser"] = "7b2e4c61-9d3a-4f5e-8a1b-2c6d9e0f1a01"
clients["outlet-admin"].setdefault("authenticationFlowBindingOverrides", {})["browser"] = "7b2e4c61-9d3a-4f5e-8a1b-2c6d9e0f1a02"
p.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n")
EOF
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~SsoAndAccessTests`
Expected: PASS (7 tests). If the MFA enrolment test lands on Keycloak's TOTP page again ("Invalid authenticator code"), the code key is wrong. Keycloak hashes the UTF-8 bytes of the `totpSecret` hidden field, so check `Totp.Code` uses exactly that.

Then run: `dotnet test`
Expected: all pass. Plan 1–3 tests get tokens by password grant through the realm's default direct-grant flow, which is unchanged, and use no seeded admin with MFA.

- [ ] **Step 6: Commit**

```bash
git add Lantern.slnx keycloak/realm src/Lantern.OutletAdmin tests
git commit -m "feat: outlet admin, single sign-on, deny-unless-role flows and mfa for hq admins"
```

---

### Task 4: Single logout through Keycloak's back-channel

**Files:**
- Create: `src/Lantern.Auth/LogoutTokenValidator.cs`, `tests/Lantern.IntegrationTests/Infrastructure/BackchannelRelay.cs`
- Modify: `src/Lantern.Auth/BffEndpoints.cs`, `src/Lantern.Auth/LanternBffSetup.cs`, `tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`, `tests/Lantern.IntegrationTests/Infrastructure/BffAppFactory.cs`, `keycloak/realm/lantern-realm.json`
- Test: `tests/Lantern.IntegrationTests/SingleLogoutTests.cs`

**Interfaces:**
- Consumes: `ServerSessionStore.RemoveBySidAsync` (Task 1), the OIDC handler's `ConfigurationManager` (Task 2), `AppBrowser`, `Totp`, `BffApps` (Tasks 2–3).
- Produces:
  - `LogoutTokenValidator.ValidateAsync(string? logoutToken, CancellationToken)` → `string?` (the `sid`, or null if invalid).
  - `POST /bff/backchannel-logout` (form `logout_token`) → 200 after removing sessions, 400 if the token is invalid.
  - Test infrastructure:
    - `BackchannelRelay` with `Port` and `Route(string clientId, HttpMessageHandler)`
    - fixture property `Relay`
    - test-realm backchannel URLs `http://host.docker.internal:{Relay.Port}/{clientId}`
    - `BffAppFactory` routes the relay to itself when its server starts
  - Realm (compose): `backchannel.logout.url` set to `http://backoffice:8080/bff/backchannel-logout` and `http://outlet-admin:8080/bff/backchannel-logout`, with session required.

- [ ] **Step 1: Write the relay and wire it into the fixture and factory**

`tests/Lantern.IntegrationTests/Infrastructure/BackchannelRelay.cs`:
```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// Keycloak runs in a container and can't reach the in-memory apps. It posts back-channel logout calls here
/// (host.docker.internal:{Port}/{clientId}); the relay forwards them to whichever in-memory app routed itself.
/// </summary>
public sealed class BackchannelRelay : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, HttpMessageHandler> _routes = new();
    private WebApplication? _app;

    public int Port { get; private set; }

    public void Route(string clientId, HttpMessageHandler handler) => _routes[clientId] = handler;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://0.0.0.0:0");
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.MapPost("/{clientId}", async (string clientId, HttpRequest request) =>
        {
            if (!_routes.TryGetValue(clientId, out var handler)) return Results.StatusCode(503);
            var form = await request.ReadFormAsync();
            using var forward = new HttpRequestMessage(HttpMethod.Post, "http://relayed/bff/backchannel-logout")
            {
                Content = new FormUrlEncodedContent(form.ToDictionary(f => f.Key, f => f.Value.ToString()))
            };
            using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
            using var response = await invoker.SendAsync(forward, CancellationToken.None);
            return Results.StatusCode((int)response.StatusCode);
        });
        await _app.StartAsync();
        Port = new Uri(_app.Urls.First()).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }
}
```

`tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`:
- add the property next to `Http`:
```csharp
    public BackchannelRelay Relay { get; } = new();
```
- in `InitializeAsync`, as its first line:
```csharp
        await Relay.StartAsync();
```
- in the `KeycloakBuilder` chain, after `.WithNetwork(_network)`, add:
```csharp
            .WithExtraHost("host.docker.internal", "host-gateway")
```
- change the realm-mapping call from `BuildTestRealm(realmPath)` to `BuildTestRealm(realmPath, Relay.Port)`, and change `BuildTestRealm`'s signature and body start to:
```csharp
    private static string BuildTestRealm(string path, int relayPort)
    {
        var realm = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        realm["sslRequired"] = "none";
        foreach (var client in realm["clients"]!.AsArray())
        {
            var clientId = (string?)client!["clientId"];
            if (clientId is "backoffice" or "outlet-admin")
                client["attributes"]!["backchannel.logout.url"] = $"http://host.docker.internal:{relayPort}/{clientId}";
        }
```
  Keep the rest of the existing body.
- in `DisposeAsync`, add as the last line:
```csharp
        await Relay.DisposeAsync();
```

`tests/Lantern.IntegrationTests/Infrastructure/BffAppFactory.cs`: replace the `Handler()` method with:
```csharp
    /// <summary>A handler into the in-memory app, and Keycloak's back-channel calls for this client routed to it.</summary>
    public HttpMessageHandler Handler()
    {
        kc.Relay.Route(clientId, Server.CreateHandler());
        return Server.CreateHandler();
    }
```

- [ ] **Step 2: Write the failing tests**

`tests/Lantern.IntegrationTests/SingleLogoutTests.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Scenario B (spec §5.2) and deactivation reaching open sessions (spec §5.4).</summary>
[Collection(KeycloakCollection.Name)]
public sealed class SingleLogoutTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private BffAppFactory<Lantern.OutletAdmin.Program> _oa = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc);
        _oa = BffApps.OutletAdmin(kc);
        return Task.CompletedTask;
    }

    private AppBrowser NewBrowser() => new((AppBrowser.BackOfficeOrigin, _bo.Handler()), (AppBrowser.OutletAdminOrigin, _oa.Handler()));

    [Fact]
    public async Task Signing_out_of_back_office_signs_you_out_of_outlet_admin()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        using var browser = NewBrowser();
        var home = await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: new Totp());
        await browser.GetAsync(AppBrowser.OutletAdminOrigin + "/");
        var signOut = System.Net.WebUtility.HtmlDecode(Regex.Match(home.Html, "href=\"(/bff/logout\\?sid=[^\"]+)\"").Groups[1].Value);

        await browser.GetAsync(AppBrowser.BackOfficeOrigin + signOut);

        Assert.True(AppBrowser.IsKeycloakLogin(await browser.GetAsync(AppBrowser.OutletAdminOrigin + "/")));
        Assert.True(AppBrowser.IsKeycloakLogin(await browser.GetAsync(AppBrowser.BackOfficeOrigin + "/")));
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_open_back_office_session()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        using var browser = NewBrowser();
        await browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", username);

        var deactivate = await (await kc.Api.ClientAsAsync("aisha.admin")).PostAsync($"/staff/{id}/deactivate", null);

        Assert.True(deactivate.IsSuccessStatusCode);
        Assert.True(AppBrowser.IsKeycloakLogin(await browser.GetAsync(AppBrowser.BackOfficeOrigin + "/")));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("")]
    public async Task Logout_token_that_is_not_from_keycloak_is_refused(string token)
    {
        using var client = _bo.CreateClient();
        var response = await client.PostAsync("/bff/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = token }));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_token_meant_for_another_client_is_refused()
    {
        // A token signed by Keycloak but issued to another audience: the test-runner's access token.
        var foreign = await kc.GetUserTokenAsync("chloe.staff");
        using var client = _bo.CreateClient();

        var response = await client.PostAsync("/bff/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = foreign }));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    public async Task DisposeAsync()
    {
        await _bo.DisposeAsync();
        await _oa.DisposeAsync();
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~SingleLogoutTests`
Expected: FAIL.
- The logout and deactivation tests find Outlet Admin and Back Office still signed in: no back-channel endpoint exists, so the relay gets 404 from the app.
- The two refusal tests get 404 instead of 400.

- [ ] **Step 4: Implement the validator and the endpoint**

`src/Lantern.Auth/LogoutTokenValidator.cs`:
```csharp
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lantern.Auth;

/// <summary>OIDC Back-Channel Logout 1.0 §2.6 checks for Keycloak's logout_token (spec §5.2).</summary>
public sealed class LogoutTokenValidator(IOptionsMonitor<OpenIdConnectOptions> oidc, IOptions<LanternBffOptions> bff)
{
    private const string BackchannelEvent = "http://schemas.openid.net/event/backchannel-logout";

    /// <returns>The Keycloak session id to end, or null when the token must be refused.</returns>
    public async Task<string?> ValidateAsync(string? logoutToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logoutToken)) return null;

        var options = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(ct);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(logoutToken, new TokenValidationParameters
        {
            ValidIssuer = bff.Value.Issuer,
            ValidAudience = bff.Value.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            RequireExpirationTime = false,
            ValidateLifetime = true
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token) return null;

        var hasEvent = token.TryGetPayloadValue<Dictionary<string, object>>("events", out var events) && events.ContainsKey(BackchannelEvent);
        var hasNonce = token.TryGetPayloadValue<string>("nonce", out _);
        var hasSid = token.TryGetPayloadValue<string>("sid", out var sid) && !string.IsNullOrEmpty(sid);
        return hasEvent && !hasNonce && hasSid ? sid : null;
    }
}
```

`src/Lantern.Auth/LanternBffSetup.cs`: after `services.AddSingleton<ServerSessionStore>();` add:
```csharp
        services.AddSingleton<LogoutTokenValidator>();
```

`src/Lantern.Auth/BffEndpoints.cs`: add before `return app;`:
```csharp
        // Keycloak calls this for every client of a session that ended anywhere (spec §5.2).
        app.MapPost("/bff/backchannel-logout", async (HttpContext http, LogoutTokenValidator validator, ServerSessionStore sessions) =>
            {
                var form = await http.Request.ReadFormAsync(http.RequestAborted);
                var sid = await validator.ValidateAsync(form["logout_token"], http.RequestAborted);
                if (sid is null) return Results.BadRequest();
                await sessions.RemoveBySidAsync(sid);
                return Results.Ok();
            })
            .DisableAntiforgery()
            .AllowAnonymous();
```

- [ ] **Step 5: Put the compose back-channel URLs in the realm**

```bash
python3 - <<'EOF'
import json, pathlib
p = pathlib.Path("keycloak/realm/lantern-realm.json")
realm = json.loads(p.read_text())
for client in realm["clients"]:
    if client["clientId"] in ("backoffice", "outlet-admin"):
        client["frontchannelLogout"] = False
        client["attributes"]["backchannel.logout.url"] = f"http://{client['clientId']}:8080/bff/backchannel-logout"
        client["attributes"]["backchannel.logout.session.required"] = "true"
        client["attributes"]["backchannel.logout.revoke.offline.tokens"] = "false"
p.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n")
EOF
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~SingleLogoutTests`
Expected: PASS (5 tests). If the logout tests still find a live session:
1. Check the Keycloak container logs for `Backchannel logout … failed`. That means the container can't reach `host.docker.internal:{port}`; the relay listens on `0.0.0.0`, and the fixture adds the `host-gateway` extra host.
2. If the relay was reached but the app returned 400, print the validator's `result.Exception` while debugging. The likely cause is the audience: Keycloak sets `aud` to the client id.

Then run: `dotnet test`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add keycloak/realm src/Lantern.Auth tests
git commit -m "feat: single logout through keycloak back-channel"
```

---

### Task 5: API calls with the user's token, and the Back Office pages

**Files:**
- Create: `src/Lantern.Auth/AccessTokenProvider.cs`, `src/Lantern.Auth/LanternApi.cs`, `src/Lantern.BackOffice/Components/Pages/Staff.razor`, `src/Lantern.BackOffice/Components/Pages/Promotions.razor`
- Modify: `src/Lantern.Auth/LanternBffSetup.cs`, `src/Lantern.BackOffice/Components/Pages/Home.razor`, `src/Lantern.BackOffice/wwwroot/app.css`
- Test: `tests/Lantern.IntegrationTests/BackOfficePageTests.cs`

**Interfaces:**
- Consumes: `ServerSessionStore` and `ServerSessionStore.SessionClaim` (Task 1), `LanternBffOptions`, the named HttpClients `"lantern-api"` and `"keycloak"`, and `TimeProvider` (Task 2). API routes from Plans 1–2: `/dashboard`, `/staff`, `/promotions`.
- Produces:
  - `AccessTokenProvider.GetAccessTokenAsync(ClaimsPrincipal, CancellationToken)` → `string?`. It refreshes when within 30 s of expiry. If the refresh fails, it removes the session and returns null.
  - `ApiResult<T>(int Status, T? Value, string? Policy)`, with `Ok` = 2xx.
  - `LanternApi.GetAsync<T>(user, path, ct)` and `PostAsync<T>(user, path, body, ct)`. A 401, from a missing or expired session, comes back as status 401.
  - Back Office pages:
    - `/` (dashboard greeting + role nav)
    - `/staff` (hq-admin: list, create, deactivate, reset password)
    - `/promotions` (list; marketing can create; others get a "Try it anyway" button that shows the API's 403 policy)

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/BackOfficePageTests.cs`:
```csharp
using System.Net.Http.Json;
using Lantern.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Lantern.IntegrationTests;

/// <summary>Back Office pages per role (scenario C in the UI), and token refresh behind them.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class BackOfficePageTests(KeycloakFixture kc) : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private AppBrowser _browser = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc, s => s.AddSingleton<TimeProvider>(_time));
        _browser = new AppBrowser((AppBrowser.BackOfficeOrigin, _bo.Handler()));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Home_greets_the_user_with_data_from_the_api()
    {
        var page = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");
        Assert.Contains("Hello, Chloe Wong", page.Html);
    }

    [Fact]
    public async Task Staff_page_lists_staff_for_an_hq_admin()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", admin, totp: new Totp());

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/staff");

        Assert.Contains("data-testid=\"staff-eric.procure\"", page.Html);
        Assert.Contains("data-testid=\"create-staff\"", page.Html);
    }

    [Fact]
    public async Task Staff_page_is_off_limits_to_other_hq_staff()
    {
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "dina.marketing");

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/staff");

        Assert.Contains("data-testid=\"access-denied\"", page.Html);
        Assert.DoesNotContain("staff-eric.procure", page.Html);
    }

    [Fact]
    public async Task Marketing_sees_promotions_and_the_create_form()
    {
        await (await kc.Api.ClientAsAsync("dina.marketing")).PostAsJsonAsync("/promotions", new { name = "Merdeka Mornings", startsOn = "2026-08-31" });
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "dina.marketing");

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/promotions");

        Assert.Contains("Merdeka Mornings", page.Html);
        Assert.Contains("data-testid=\"create-promotion\"", page.Html);
    }

    [Fact]
    public async Task Other_staff_see_promotions_with_a_try_anyway_button_instead_of_the_form()
    {
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");

        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/promotions");

        Assert.DoesNotContain("data-testid=\"create-promotion\"", page.Html);
        Assert.Contains("data-testid=\"try-anyway\"", page.Html);
    }

    [Fact]
    public async Task Pages_keep_working_after_the_access_token_expires()
    {
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "chloe.staff");

        _time.Advance(TimeSpan.FromMinutes(6)); // access token (5 min) now expired; refresh token still valid
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");

        Assert.Contains("Hello, Chloe Wong", page.Html);
    }

    [Fact]
    public async Task Ended_keycloak_session_sends_the_user_to_sign_in_on_the_next_api_call()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", username);
        kc.Relay.Route("backoffice", new SwallowHandler()); // drop the back-channel call: only the token refresh can notice
        await kc.LogoutUserAsync(id);

        _time.Advance(TimeSpan.FromMinutes(6));
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");

        Assert.True(AppBrowser.IsKeycloakLogin(page));
    }

    private sealed class SwallowHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    public async Task DisposeAsync()
    {
        _browser.Dispose();
        await _bo.DisposeAsync();
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~BackOfficePageTests`
Expected: FAIL. Neither "Hello, Chloe Wong" nor the `staff-…`, `create-promotion` or `try-anyway` markers are present, and `/staff` and `/promotions` are 404 pages.

- [ ] **Step 3: Implement token access and the API client**

`src/Lantern.Auth/AccessTokenProvider.cs`:
```csharp
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Lantern.Auth;

/// <summary>
/// The signed-in user's access token, read from the server session and refreshed when near expiry.
/// A failed refresh (session ended at Keycloak) removes the server session, so the next page load signs in.
/// </summary>
public sealed class AccessTokenProvider(ServerSessionStore sessions, IHttpClientFactory http, IOptions<LanternBffOptions> options, TimeProvider time)
{
    public async Task<string?> GetAccessTokenAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.FindFirstValue(ServerSessionStore.SessionClaim) is not { } key) return null;
        if (await sessions.RetrieveAsync(key) is not { } ticket) return null;

        var properties = ticket.Properties;
        var access = properties.GetTokenValue("access_token");
        var expiresAt = DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var at) ? at : DateTimeOffset.MinValue;
        if (access is not null && expiresAt - time.GetUtcNow() > TimeSpan.FromSeconds(30)) return access;

        var refreshed = await RefreshAsync(properties.GetTokenValue("refresh_token"), ct);
        if (refreshed is null)
        {
            await sessions.RemoveAsync(key);
            return null;
        }

        properties.UpdateTokenValue("access_token", refreshed.Value.Access);
        if (refreshed.Value.Refresh is { } refresh) properties.UpdateTokenValue("refresh_token", refresh);
        properties.UpdateTokenValue("expires_at",
            time.GetUtcNow().AddSeconds(refreshed.Value.ExpiresIn).ToString("o", CultureInfo.InvariantCulture));
        await sessions.RenewAsync(key, ticket);
        return refreshed.Value.Access;
    }

    private async Task<(string Access, string? Refresh, int ExpiresIn)?> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) return null;
        var o = options.Value;
        try
        {
            using var response = await http.CreateClient(LanternBffSetup.KeycloakClient).PostAsync(
                $"{o.RealmUrl}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = o.ClientId,
                    ["client_secret"] = o.ClientSecret
                }), ct);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var body = json.RootElement;
            return (body.GetProperty("access_token").GetString()!,
                body.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
                body.GetProperty("expires_in").GetInt32());
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
```

`src/Lantern.Auth/LanternApi.cs`:
```csharp
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
```

`src/Lantern.Auth/LanternBffSetup.cs`: after `services.AddSingleton<LogoutTokenValidator>();` add:
```csharp
        services.AddSingleton<AccessTokenProvider>();
        services.AddSingleton<LanternApi>();
```

- [ ] **Step 4: Implement the pages**

`src/Lantern.BackOffice/Components/Pages/Home.razor` (replace):
```razor
@page "/"
@attribute [Authorize]
@inject LanternApi Api
@inject NavigationManager Navigation

<PageTitle>Lantern Back Office</PageTitle>

<h1>Back Office</h1>
<p class="greeting" data-testid="greeting">@greeting</p>

<nav class="cards">
    @if (roles.Contains(LanternRoles.HqAdmin))
    {
        <a class="card" href="/staff" data-testid="nav-staff">Staff</a>
    }
    <a class="card" href="/promotions" data-testid="nav-promotions">Promotions</a>
</nav>

<section data-testid="roles">
    <h2>Your roles</h2>
    <ul>
        @foreach (var role in roles)
        {
            <li data-testid="role-@role">@role</li>
        }
    </ul>
</section>

@code {
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    private string[] roles = [];
    private string? greeting;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthState).User;
        roles = LanternRoles.Of(user);
        var dashboard = await Api.GetAsync<DashboardDto>(user, "/dashboard");
        if (dashboard.Status == 401) Navigation.NavigateTo("/bff/login?returnUrl=/", forceLoad: true);
        greeting = dashboard.Value?.Greeting;
    }

    private sealed record DashboardDto(string Greeting, string[] Outlets);
}
```

`src/Lantern.BackOffice/Components/Pages/Staff.razor`:
```razor
@page "/staff"
@attribute [Authorize(Roles = LanternRoles.HqAdmin)]
@rendermode InteractiveServer
@inject LanternApi Api

<PageTitle>Staff</PageTitle>

<h1>Staff</h1>

<form class="card" data-testid="create-staff" @onsubmit="CreateAsync">
    <h2>Add a staff member</h2>
    <label>Username <input @bind="newUsername" data-testid="new-username" /></label>
    <label>First name <input @bind="newFirst" data-testid="new-first" /></label>
    <label>Last name <input @bind="newLast" data-testid="new-last" /></label>
    <label>Email <input type="email" @bind="newEmail" data-testid="new-email" /></label>
    <label>Department
        <select @bind="newDepartment" data-testid="new-department">
            @foreach (var d in new[] { "Marketing", "Procurement", "Finance", "Admin" })
            {
                <option value="@d">@d</option>
            }
        </select>
    </label>
    <button class="primary" type="submit" data-testid="create-staff-submit">Add and email an invite</button>
</form>

@if (message is not null)
{
    <p role="status" data-testid="staff-message">@message</p>
}

<table class="table">
    <thead><tr><th>Name</th><th>Username</th><th>Roles</th><th>Status</th><th></th></tr></thead>
    <tbody>
        @foreach (var s in staff)
        {
            <tr data-testid="staff-@s.Username">
                <td>@s.Name</td>
                <td>@s.Username</td>
                <td>@string.Join(", ", s.Roles)</td>
                <td>@(s.Enabled ? "Active" : "Deactivated")</td>
                <td>
                    @if (s.Enabled)
                    {
                        <button @onclick="() => DeactivateAsync(s)" data-testid="deactivate-@s.Username">Deactivate</button>
                    }
                    <button @onclick="() => ResetPasswordAsync(s)" data-testid="reset-@s.Username">Reset password</button>
                </td>
            </tr>
        }
    </tbody>
</table>

@code {
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    private List<StaffDto> staff = [];
    private string? message;
    private string newUsername = "", newFirst = "", newLast = "", newEmail = "", newDepartment = "Marketing";

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var result = await Api.GetAsync<List<StaffDto>>((await AuthState).User, "/staff");
        staff = result.Value ?? [];
    }

    private async Task CreateAsync()
    {
        var result = await Api.PostAsync<object>((await AuthState).User, "/staff", new
        {
            username = newUsername, firstName = newFirst, lastName = newLast, email = newEmail, departments = new[] { newDepartment }
        });
        message = result.Ok ? $"Added {newUsername}; they've been emailed an invite." : $"Couldn't add {newUsername} ({result.Status}).";
        if (result.Ok) (newUsername, newFirst, newLast, newEmail) = ("", "", "", "");
        await LoadAsync();
    }

    private async Task DeactivateAsync(StaffDto s)
    {
        var result = await Api.PostAsync<object>((await AuthState).User, $"/staff/{s.Id}/deactivate", null);
        message = result.Ok ? $"{s.Name} has been deactivated and signed out everywhere." : $"Couldn't deactivate {s.Name} ({result.Status}).";
        await LoadAsync();
    }

    private async Task ResetPasswordAsync(StaffDto s)
    {
        var result = await Api.PostAsync<object>((await AuthState).User, $"/staff/{s.Id}/reset-password", null);
        message = result.Ok ? $"Password reset email sent to {s.Name}." : $"Couldn't send the reset email ({result.Status}).";
    }

    private sealed record StaffDto(string Id, string Username, string Name, string? Email, bool Enabled, string[] Groups, string[] Roles);
}
```

`src/Lantern.BackOffice/Components/Pages/Promotions.razor`:
```razor
@page "/promotions"
@attribute [Authorize]
@rendermode InteractiveServer
@inject LanternApi Api

<PageTitle>Promotions</PageTitle>

<h1>Promotions</h1>

@if (isMarketing)
{
    <form class="card" data-testid="create-promotion" @onsubmit="CreateAsync">
        <label>Name <input @bind="newName" data-testid="promotion-name" /></label>
        <label>Starts on <input type="date" @bind="newStartsOn" data-testid="promotion-starts" /></label>
        <button class="primary" type="submit" data-testid="promotion-submit">Create promotion</button>
    </form>
}
else
{
    <p class="card">Only Marketing can create promotions. The API enforces this, not just the screen:
        <button @onclick="TryAnywayAsync" data-testid="try-anyway">Try it anyway</button>
    </p>
}

@if (message is not null)
{
    <p role="status" data-testid="promotion-message">@message</p>
}

@if (promotions.Count == 0)
{
    <p data-testid="no-promotions">No promotions yet.</p>
}
<ul data-testid="promotions">
    @foreach (var p in promotions)
    {
        <li>@p.Name — from @p.StartsOn.ToString("d MMM yyyy") <span class="muted">by @p.CreatedBy</span></li>
    }
</ul>

@code {
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;

    private List<PromotionDto> promotions = [];
    private bool isMarketing;
    private string? message;
    private string newName = "";
    private DateOnly newStartsOn = DateOnly.FromDateTime(DateTime.Today);

    protected override async Task OnInitializedAsync()
    {
        isMarketing = LanternRoles.Of((await AuthState).User).Contains(LanternRoles.Marketing);
        await LoadAsync();
    }

    private async Task LoadAsync() =>
        promotions = (await Api.GetAsync<List<PromotionDto>>((await AuthState).User, "/promotions")).Value ?? [];

    private async Task CreateAsync()
    {
        var result = await Api.PostAsync<PromotionDto>((await AuthState).User, "/promotions", new { name = newName, startsOn = newStartsOn });
        message = result.Ok ? $"Created {newName}." : $"Couldn't create it ({result.Status}).";
        if (result.Ok) newName = "";
        await LoadAsync();
    }

    private async Task TryAnywayAsync()
    {
        var result = await Api.PostAsync<PromotionDto>((await AuthState).User, "/promotions", new { name = "Sneaky sale", startsOn = DateOnly.FromDateTime(DateTime.Today) });
        message = result.Status == 403
            ? $"The API refused: 403 Forbidden by policy \"{result.Policy}\"."
            : $"Unexpected answer from the API: {result.Status}.";
    }

    private sealed record PromotionDto(Guid Id, string Name, DateOnly StartsOn, string CreatedBy);
}
```

`src/Lantern.BackOffice/wwwroot/app.css` (replace):
```css
:root { --bg: #f5f6f8; --card: #fff; --ink: #1d2433; --muted: #5b6475; --accent: #1f4e8c; --line: #dde1e8; }
* { box-sizing: border-box; }
body { margin: 0; font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; background: var(--bg); color: var(--ink); }
.app-bar { display: flex; gap: 1rem; align-items: center; padding: 0.75rem 1.5rem; background: var(--accent); color: #fff; }
.app-bar a { color: #fff; }
.app-bar .who { margin-left: auto; }
.page { max-width: 64rem; margin: 0 auto; padding: 1.5rem; }
.card { display: block; background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 1rem 1.25rem; margin: 0.75rem 0; color: inherit; text-decoration: none; }
.cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr)); gap: 0.75rem; }
label { display: block; margin: 0.5rem 0; }
input, select { display: block; width: 100%; padding: 0.45rem; border: 1px solid var(--line); border-radius: 6px; font: inherit; }
button { font: inherit; padding: 0.4rem 0.9rem; border-radius: 6px; border: 1px solid var(--line); background: var(--card); cursor: pointer; }
.primary { background: var(--accent); border-color: var(--accent); color: #fff; }
.table { width: 100%; border-collapse: collapse; background: var(--card); }
.table th, .table td { text-align: left; padding: 0.5rem; border-bottom: 1px solid var(--line); }
.muted { color: var(--muted); }
.greeting { font-size: 1.25rem; }
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~BackOfficePageTests`
Expected: PASS (7 tests). If `Pages_keep_working_after_the_access_token_expires` fails, check whether the OIDC handler wrote `expires_at` with the fake clock. Inject `TimeProvider` into the OIDC and cookie options (`o.TimeProvider = time`) inside `AddLanternBff`'s `Configure` calls, and record a ruling.

Then run: `dotnet test`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/Lantern.Auth src/Lantern.BackOffice tests
git commit -m "feat: back office pages calling the api with refreshed user tokens"
```

---

### Task 6: Outlet Admin pages (stock and roster, the Tills page)

**Files:**
- Create: `src/Lantern.OutletAdmin/Components/Pages/Tills.razor`
- Modify: `src/Lantern.OutletAdmin/Components/Pages/Home.razor`, `src/Lantern.OutletAdmin/wwwroot/app.css`
- Test: `tests/Lantern.IntegrationTests/OutletAdminPageTests.cs`

**Interfaces:**
- Consumes: `LanternApi`, `LanternRoles` (Tasks 2 and 5); API `/outlets/{id}/stock`, `/outlets/{id}/roster`, `/outlets/{id}/tills`, `DELETE /outlets/{id}/tills/{userId}/session` (Plans 1–2); fixture `OpenTillAsync` (Plan 2).
- Produces:
  - `/` (stock and roster for the user's outlet; `hq-admin` picks one with `?outlet=`)
  - `/tills` (the outlet's till accounts, with who is signed in, and **Release** buttons)

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/OutletAdminPageTests.cs`:
```csharp
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class OutletAdminPageTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.OutletAdmin.Program> _oa = null!;
    private AppBrowser _browser = null!;

    public Task InitializeAsync()
    {
        _oa = BffApps.OutletAdmin(kc);
        _browser = new AppBrowser((AppBrowser.OutletAdminOrigin, _oa.Handler()));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Manager_sees_their_outlets_stock_and_roster()
    {
        var page = await _browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");

        Assert.Contains("data-testid=\"outlet\">BGS", page.Html);
        Assert.Contains("data-testid=\"stock-SKU-100\"", page.Html);
        Assert.Contains("42", page.Html);
        Assert.Contains("Siti Aminah", page.Html);
    }

    [Fact]
    public async Task Manager_cannot_switch_to_another_outlet()
    {
        await _browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");

        var page = await _browser.GetAsync(AppBrowser.OutletAdminOrigin + "/?outlet=KLC");

        Assert.Contains("data-testid=\"outlet\">BGS", page.Html);
        Assert.DoesNotContain("Mei Ling Chan", page.Html);
    }

    [Fact]
    public async Task Hq_admin_can_pick_any_outlet()
    {
        var (_, admin, _) = await kc.CreateTempUserAsync("/HQ/Admin");
        await _browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", admin, totp: new Totp());

        var page = await _browser.GetAsync(AppBrowser.OutletAdminOrigin + "/?outlet=KLC");

        Assert.Contains("data-testid=\"outlet\">KLC", page.Html);
        Assert.Contains("Mei Ling Chan", page.Html);
    }

    [Fact]
    public async Task Tills_page_shows_which_till_accounts_are_signed_in()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        await _browser.SignInAsync(AppBrowser.OutletAdminOrigin + "/", "mgr.bangsar");

        var page = await _browser.GetAsync(AppBrowser.OutletAdminOrigin + "/tills");

        Assert.Contains($"data-testid=\"till-{till.Account}\"", page.Html);
        Assert.Contains($"data-testid=\"release-{till.Account}\"", page.Html);
        Assert.Contains("data-testid=\"till-outlet-bangsar-2\"", page.Html);
        Assert.DoesNotContain("data-testid=\"release-outlet-bangsar-2\"", page.Html);
    }

    public async Task DisposeAsync()
    {
        _browser.Dispose();
        await _oa.DisposeAsync();
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~OutletAdminPageTests`
Expected: FAIL. The home page has only roles (no `outlet`, `stock-…` markers), and `/tills` is a 404 page.

- [ ] **Step 3: Implement the pages**

`src/Lantern.OutletAdmin/Components/Pages/Home.razor` (replace):
```razor
@page "/"
@attribute [Authorize]
@inject LanternApi Api

<PageTitle>Lantern Outlet Admin</PageTitle>

<h1>Outlet <span data-testid="outlet">@outletId</span></h1>

@if (isAdmin)
{
    <nav class="outlet-picker">
        @foreach (var id in new[] { "BGS", "KLC", "PJY" })
        {
            <a href="/?outlet=@id" data-testid="pick-@id">@id</a>
        }
    </nav>
}

<p><a href="/tills?outlet=@outletId" data-testid="nav-tills">Tills</a></p>

<section class="card">
    <h2>Stock</h2>
    <table class="table">
        <thead><tr><th>SKU</th><th>Item</th><th>Quantity</th></tr></thead>
        <tbody>
            @foreach (var line in stock)
            {
                <tr data-testid="stock-@line.Sku"><td>@line.Sku</td><td>@line.Name</td><td>@line.Quantity</td></tr>
            }
        </tbody>
    </table>
</section>

<section class="card">
    <h2>Roster</h2>
    <ul>
        @foreach (var person in roster)
        {
            <li>@person.Name <span class="muted">(@person.Role)</span></li>
        }
    </ul>
</section>

@code {
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "outlet")] private string? RequestedOutlet { get; set; }

    private string outletId = "";
    private bool isAdmin;
    private List<StockLine> stock = [];
    private List<RosterEntry> roster = [];

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthState).User;
        isAdmin = LanternRoles.Of(user).Contains(LanternRoles.HqAdmin);
        // Managers always see their own outlet; only hq-admin may choose (plan decision 2).
        outletId = isAdmin ? (RequestedOutlet?.ToUpperInvariant() ?? "BGS") : user.FindFirst("outlet_id")?.Value ?? "";
        stock = (await Api.GetAsync<List<StockLine>>(user, $"/outlets/{outletId}/stock")).Value ?? [];
        roster = (await Api.GetAsync<List<RosterEntry>>(user, $"/outlets/{outletId}/roster")).Value ?? [];
    }

    private sealed record StockLine(string Sku, string Name, int Quantity);
    private sealed record RosterEntry(string Name, string Role);
}
```

`src/Lantern.OutletAdmin/Components/Pages/Tills.razor`:
```razor
@page "/tills"
@attribute [Authorize(Roles = $"{LanternRoles.OutletManager},{LanternRoles.HqAdmin}")]
@rendermode InteractiveServer
@inject LanternApi Api

<PageTitle>Tills</PageTitle>

<h1>Tills at <span data-testid="outlet">@outletId</span></h1>
<p class="muted">Each till signs in with its own account. Release an account if its till was lost, replaced or wiped,
    so it can sign in on another till.</p>

@if (message is not null)
{
    <p role="status" data-testid="tills-message">@message</p>
}

<table class="table">
    <thead><tr><th>Till account</th><th>Signed in</th><th>Last used</th><th>From</th><th></th></tr></thead>
    <tbody>
        @foreach (var till in tills)
        {
            <tr data-testid="till-@till.Username">
                <td>@till.Username</td>
                @if (till.Session is { } s)
                {
                    <td>@s.StartedAt.ToLocalTime().ToString("d MMM HH:mm")</td>
                    <td>@s.LastUsedAt.ToLocalTime().ToString("d MMM HH:mm")</td>
                    <td>@s.IpAddress</td>
                    <td><button @onclick="() => ReleaseAsync(till)" data-testid="release-@till.Username">Release</button></td>
                }
                else
                {
                    <td colspan="4" class="muted">Not signed in</td>
                }
            </tr>
        }
    </tbody>
</table>

@code {
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "outlet")] private string? RequestedOutlet { get; set; }

    private string outletId = "";
    private List<TillDto> tills = [];
    private string? message;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthState).User;
        var isAdmin = LanternRoles.Of(user).Contains(LanternRoles.HqAdmin);
        outletId = isAdmin ? (RequestedOutlet?.ToUpperInvariant() ?? "BGS") : user.FindFirst("outlet_id")?.Value ?? "";
        await LoadAsync();
    }

    private async Task LoadAsync() =>
        tills = (await Api.GetAsync<List<TillDto>>((await AuthState).User, $"/outlets/{outletId}/tills")).Value ?? [];

    private async Task ReleaseAsync(TillDto till)
    {
        var result = await Api.SendDeleteAsync((await AuthState).User, $"/outlets/{outletId}/tills/{till.Id}/session");
        message = result.Ok ? $"{till.Username} released. It can now sign in on another till." : $"Couldn't release {till.Username} ({result.Status}).";
        await LoadAsync();
    }

    private sealed record TillSessionDto(DateTimeOffset StartedAt, DateTimeOffset LastUsedAt, string? IpAddress);
    private sealed record TillDto(string Id, string Username, bool Enabled, TillSessionDto? Session);
}
```

`src/Lantern.Auth/LanternApi.cs`: add a public method next to `PostAsync`:
```csharp
    public Task<ApiResult<object>> SendDeleteAsync(ClaimsPrincipal user, string path, CancellationToken ct = default) =>
        SendAsync<object>(user, HttpMethod.Delete, path, null, ct);
```

`src/Lantern.OutletAdmin/wwwroot/app.css`: copy Back Office's `app.css` from Task 5, changing the accent colour to `--accent: #0f766e;`, and add:
```css
.outlet-picker { display: flex; gap: 0.75rem; margin: 0.5rem 0 1rem; }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~OutletAdminPageTests`
Expected: PASS (4 tests).
Then run: `dotnet test`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Lantern.Auth src/Lantern.OutletAdmin tests
git commit -m "feat: outlet admin stock, roster and tills pages"
```

---

### Task 7: Branded login pages per app

**Files:**
- Create: `keycloak/themes/lantern-hq/login/theme.properties`, `…/resources/css/lantern.css`, `…/messages/messages_en.properties`, and the same three files for `lantern-outlet` and `lantern-till`
- Modify: `keycloak/Dockerfile`, `keycloak/realm/lantern-realm.json`
- Test: `tests/Lantern.IntegrationTests/LoginThemeTests.cs`

**Interfaces:**
- Consumes: `AppBrowser`, `BffApps` (Tasks 2–3), `OidcBrowser.TillClientId` (Plan 2).
- Produces:
  - Themes `lantern-hq`, `lantern-outlet` and `lantern-till`. Each extends `keycloak.v2` and marks the page with `kcHtmlClass=login-pf lantern-<name>`, its own stylesheet, and its own sign-in title.
  - Client attributes `login_theme`: backoffice → `lantern-hq`, outlet-admin → `lantern-outlet`, till → `lantern-till`.

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/LoginThemeTests.cs`:
```csharp
using System.Net;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Scenario H (spec §5.7): each app's Keycloak pages carry its own theme.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class LoginThemeTests(KeycloakFixture kc) : IAsyncLifetime
{
    private BffAppFactory<Lantern.BackOffice.Program> _bo = null!;
    private BffAppFactory<Lantern.OutletAdmin.Program> _oa = null!;
    private AppBrowser _browser = null!;

    public Task InitializeAsync()
    {
        _bo = BffApps.BackOffice(kc);
        _oa = BffApps.OutletAdmin(kc);
        _browser = new AppBrowser((AppBrowser.BackOfficeOrigin, _bo.Handler()), (AppBrowser.OutletAdminOrigin, _oa.Handler()));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Back_office_login_uses_the_hq_theme()
    {
        var page = await _browser.GetAsync(AppBrowser.BackOfficeOrigin + "/");
        Assert.Contains("class=\"login-pf lantern-hq\"", page.Html);
        Assert.Contains("Sign in to Lantern Back Office", WebUtility.HtmlDecode(page.Html));
    }

    [Fact]
    public async Task Outlet_admin_login_uses_the_outlet_theme()
    {
        var page = await _browser.GetAsync(AppBrowser.OutletAdminOrigin + "/");
        Assert.Contains("class=\"login-pf lantern-outlet\"", page.Html);
        Assert.Contains("Sign in to Lantern Outlet Admin", WebUtility.HtmlDecode(page.Html));
    }

    [Fact]
    public async Task Till_login_uses_the_kiosk_theme()
    {
        var page = await _browser.GetAsync(
            $"{kc.Issuer}/protocol/openid-connect/auth?client_id={OidcBrowser.TillClientId}&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(OidcBrowser.TillRedirectUri)}&scope=openid");
        Assert.Contains("class=\"login-pf lantern-till\"", page.Html);
        Assert.Contains("Sign in this till", WebUtility.HtmlDecode(page.Html));
    }

    [Fact]
    public async Task Deny_page_is_themed_too()
    {
        var page = await _browser.SignInAsync(AppBrowser.BackOfficeOrigin + "/", "mgr.bangsar");
        Assert.Contains("class=\"login-pf lantern-hq\"", page.Html);
    }

    public async Task DisposeAsync()
    {
        _browser.Dispose();
        await _bo.DisposeAsync();
        await _oa.DisposeAsync();
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~LoginThemeTests`
Expected: FAIL. Pages show the default `class="login-pf"`, with none of the theme classes or titles.

- [ ] **Step 3: Add the themes**

`keycloak/themes/lantern-hq/login/theme.properties`:
```properties
parent=keycloak.v2
import=common/keycloak
styles=css/styles.css css/lantern.css
kcHtmlClass=login-pf lantern-hq
```

`keycloak/themes/lantern-hq/login/messages/messages_en.properties`:
```properties
loginAccountTitle=Sign in to Lantern Back Office
```

`keycloak/themes/lantern-hq/login/resources/css/lantern.css`:
```css
.lantern-hq body, .lantern-hq .pf-v5-c-login { background: #1f4e8c; }
.lantern-hq .pf-v5-c-login__main { border-top: 6px solid #f2b705; }
.lantern-hq .pf-v5-c-button.pf-m-primary { background-color: #1f4e8c; }
```

`keycloak/themes/lantern-outlet/login/theme.properties`:
```properties
parent=keycloak.v2
import=common/keycloak
styles=css/styles.css css/lantern.css
kcHtmlClass=login-pf lantern-outlet
```

`keycloak/themes/lantern-outlet/login/messages/messages_en.properties`:
```properties
loginAccountTitle=Sign in to Lantern Outlet Admin
```

`keycloak/themes/lantern-outlet/login/resources/css/lantern.css`:
```css
.lantern-outlet body, .lantern-outlet .pf-v5-c-login { background: #0f766e; }
.lantern-outlet .pf-v5-c-login__main { border-top: 6px solid #f59e0b; }
.lantern-outlet .pf-v5-c-button.pf-m-primary { background-color: #0f766e; }
```

`keycloak/themes/lantern-till/login/theme.properties`:
```properties
parent=keycloak.v2
import=common/keycloak
styles=css/styles.css css/lantern.css
kcHtmlClass=login-pf lantern-till
```

`keycloak/themes/lantern-till/login/messages/messages_en.properties`:
```properties
loginAccountTitle=Sign in this till
```

`keycloak/themes/lantern-till/login/resources/css/lantern.css`:
```css
/* Kiosk: large touch targets for a shared till screen. */
.lantern-till body, .lantern-till .pf-v5-c-login { background: #b45309; }
.lantern-till .pf-v5-c-form-control input { font-size: 1.5rem; min-height: 3.5rem; }
.lantern-till .pf-v5-c-button.pf-m-primary { font-size: 1.4rem; min-height: 4rem; background-color: #b45309; }
```

`keycloak/Dockerfile`: append to the final stage:
```dockerfile
COPY themes/ /opt/keycloak/themes/
```

```bash
python3 - <<'EOF'
import json, pathlib
p = pathlib.Path("keycloak/realm/lantern-realm.json")
realm = json.loads(p.read_text())
themes = {"backoffice": "lantern-hq", "outlet-admin": "lantern-outlet", "till": "lantern-till"}
for client in realm["clients"]:
    if client["clientId"] in themes:
        client["attributes"]["login_theme"] = themes[client["clientId"]]
p.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n")
EOF
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~LoginThemeTests`
Expected: PASS (4 tests). If the class marker is present but not the title, `keycloak.v2`'s `login.ftl` uses a different message key for the header. Grep `login.ftl` in the `keycloak-themes-26.7.5.jar` for `msg("` and use that key, then record a ruling.

Then run: `dotnet test`
Expected: all pass. The till's Plan 2–3 tests look for form ids and messages, not styling.

- [ ] **Step 5: Commit**

```bash
git add keycloak tests
git commit -m "feat: branded keycloak login themes per app"
```

---

### Task 8: Back Office and Outlet Admin in Docker Compose, smoke check and README

**Files:**
- Create: `src/Lantern.BackOffice/Dockerfile`, `src/Lantern.OutletAdmin/Dockerfile`
- Modify: `docker-compose.yml`, `scripts/smoke.sh`, `README.md`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - Compose services `backoffice` (`127.0.0.1:5200`) and `outlet-admin` (`127.0.0.1:5300`), reachable from Keycloak as `http://backoffice:8080` and `http://outlet-admin:8080` for back-channel logout.
  - The smoke check covers both apps.

- [ ] **Step 1: Write the failing smoke check**

`scripts/smoke.sh`:
- extend the loop:
```bash
for svc_port in keycloak:8080 mailpit:8025 api:8080 till:8080 backoffice:8080 outlet-admin:8080; do
```
- add before `echo "OK: stack healthy"`:
```bash
for app in 5200 5300; do
  location=$(curl -s -o /dev/null -w '%{redirect_url}' "http://localhost:$app/")
  [[ "$location" == http://localhost:8080/realms/lantern/* ]] || fail "app on :$app did not redirect to Keycloak (got '$location')"
done
```

Run: `./scripts/smoke.sh`
Expected: FAIL, with `service "backoffice" is not running` (or a similar message from `docker compose port`).

- [ ] **Step 2: Containerise both apps**

`src/Lantern.BackOffice/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Lantern.Auth/Lantern.Auth.csproj src/Lantern.Auth/
COPY src/Lantern.BackOffice/Lantern.BackOffice.csproj src/Lantern.BackOffice/
RUN dotnet restore src/Lantern.BackOffice/Lantern.BackOffice.csproj
COPY src/Lantern.Auth/ src/Lantern.Auth/
COPY src/Lantern.BackOffice/ src/Lantern.BackOffice/
RUN dotnet publish src/Lantern.BackOffice/Lantern.BackOffice.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Lantern.BackOffice.dll"]
```

`src/Lantern.OutletAdmin/Dockerfile`: the same, with `Lantern.BackOffice` replaced by `Lantern.OutletAdmin` throughout.

`docker-compose.yml`: add after `till`:
```yaml
  backoffice:
    build:
      context: .
      dockerfile: src/Lantern.BackOffice/Dockerfile
    image: lantern-backoffice:dev
    environment:
      Lantern__KeycloakBaseUrl: http://keycloak:8080
      Lantern__Issuer: http://localhost:8080/realms/lantern
      Lantern__ApiBaseUrl: http://api:8080
    ports:
      - "127.0.0.1:5200:8080"
    depends_on:
      keycloak:
        condition: service_healthy
      api:
        condition: service_started

  outlet-admin:
    build:
      context: .
      dockerfile: src/Lantern.OutletAdmin/Dockerfile
    image: lantern-outlet-admin:dev
    environment:
      Lantern__KeycloakBaseUrl: http://keycloak:8080
      Lantern__Issuer: http://localhost:8080/realms/lantern
      Lantern__ApiBaseUrl: http://api:8080
    ports:
      - "127.0.0.1:5300:8080"
    depends_on:
      keycloak:
        condition: service_healthy
      api:
        condition: service_started
```

- [ ] **Step 3: Run the smoke check and confirm it passes**

```bash
docker compose down -v
docker compose up -d --build --wait
./scripts/smoke.sh
```
Expected: `OK: issuer is …`, then `OK: stack healthy`. `down -v` re-imports the realm with the new flows, themes and back-channel URLs.

- [ ] **Step 4: Update the README**

`README.md`:
- replace the status line with:
```markdown
> Work in progress. Plans 1–4 of 5 are done: foundation, till backend, till app and web apps. Plan 5
> (browser tests, CI, scenario docs) is next. See `docs/superpowers/plans/2026-10-01-roadmap.md`.
```
- add rows to the services table:
```markdown
| Back Office (HQ staff, e.g. `dina.marketing`; admins set up an authenticator app) | http://localhost:5200 |
| Outlet Admin (managers, e.g. `mgr.bangsar`) | http://localhost:5300 |
```
- add a section before `## Tests`:
````markdown
## Try single sign-on and single logout

1. Sign in to Back Office as `aisha.admin`. Keycloak asks her to set up an authenticator app (MFA is for
   HQ admins only).
2. Open Outlet Admin in the same browser: no second sign-in.
3. Sign out of Back Office, then reload Outlet Admin: you're signed out there too (back-channel logout).
4. Try `mgr.bangsar` on Back Office: Keycloak refuses with "Your account doesn't have access to Back Office."
5. On Back Office → Promotions, a non-marketing user can press **Try it anyway** and see the API's 403.
````

- [ ] **Step 5: Commit**

```bash
git add src/Lantern.BackOffice/Dockerfile src/Lantern.OutletAdmin/Dockerfile docker-compose.yml scripts/smoke.sh README.md
git commit -m "feat: run back office and outlet admin in compose with smoke check and readme"
```
