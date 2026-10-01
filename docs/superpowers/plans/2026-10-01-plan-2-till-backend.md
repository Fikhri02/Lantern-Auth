# Lantern Auth — Plan 2: Till Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Everything the till needs from Keycloak and the API, proven end to end by integration tests that act exactly like the till's server. That covers a persistent outlet login, one account per till, cashier PIN sign-in (with lockout and temporary PINs), sales whose receipts name the cashier and the till, and the endpoints that manage till accounts and cashiers.

**Architecture:**
- **Plugin:** the Keycloak extension jar gains four authenticators (outlet session check, cashier check, PIN check, till account limit), PIN credential storage, and a small admin REST resource for setting PINs.
- **Realm:** gains two flows, `browser-till` and `till-cashier-pin`, both bound to the `till` client.
- **API:** gains till sales and receipts, till-account management (list, add, release), and cashier management (create, reset PIN). All of it goes through the existing service account.
- **Tests:** they drive Keycloak's real browser login over HTTP, with code + PKCE and a cookie jar, to obtain outlet offline tokens. Then they call the token endpoint with PINs, exactly as the till's server will.

**Tech Stack:** as Plan 1. Keycloak 26.7.5 SPI (Java 21), .NET 10 minimal API, xUnit 2.9 + Testcontainers 4.15.

**Spec:** `docs/superpowers/specs/2026-10-01-lantern-auth-design.md`, §4.5 (till client), §5.5 (cashier and till rows), §6 (all of it), §7 (till rows), §9, §12.2–12.3. The roadmap is in `docs/superpowers/plans/2026-10-01-roadmap.md`.

## Global Constraints

- Everything in Plan 1's Global Constraints still holds: versions, ports, fictional setting, demo password `Lantern!2026`, conventional commits with **no trailers**.
- Branch `irfan/till-backend`, created from `irfan/design-spec`. The repo-local `user.email` is `irfanfikhri@gmail.com`; leave it as is.
- The .NET SDK lives at `~/.dotnet`. Run every `dotnet` command with `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`.
- Keycloak SPI names used here were checked against the 26.7.5 sources: `AbstractDirectGrantAuthenticator`, `AuthenticatorUtils.getDisabledByBruteForceEventError`, `session.tokens().decode` (signature only, so expiry, issuer and type are checked by us), `session.sessions().getOfflineUserSession`, `forceChallenge` (does **not** count toward brute force), `PasswordHashProvider.encodedCredential/verify`, `RealmResourceProviderFactory`, and `AppAuthManager.BearerTokenAuthenticator`.
- Error codes the till receives are in `error_description` of the token endpoint's 400 response: `outlet_session_invalid`, `cashier_not_found`, `cashier_wrong_outlet`, `cashier_disabled`, `pin_missing`, `pin_invalid:<remaining>`, `pin_locked`, `pin_change_required`, `pin_rule_format`, `pin_rule_same_as_temporary`, `pin_rule_repeated_digit`, `pin_rule_sequence`.
- Seeded PINs: `c-1001` 1111, `c-1002` 2222, `c-2001` 3333, `c-2002` 4444, `c-3001` 5555 (temporary). Tests that **change** or **lock** a PIN use temporary cashiers, never the seeded ones.
- Tests that sign a till in always use a **temporary till account** from the fixture, never a seeded one, so the one-till-per-account rule never makes tests depend on each other.

**Spec deviations, decided here:**
1. **No `CredentialProvider` SPI.** PINs are stored through `user.credentialManager()` by a helper class (`PinCredentials`), as a hashed credential of type `lantern-pin`. A full provider adds a factory and metadata classes and no behaviour the spec needs.
2. **First-PIN change happens inside the PIN request** (`new_pin`), as spec §6.4 already says. There is no required action.
3. **`quickLoginCheckMilliSeconds` is 0.** Keycloak's "two failures within 1 s" quick lock would otherwise lock a cashier after one double-tapped wrong PIN. The 5-failure lockout still applies.
4. **The cashier token's link to the till is the `till_account` claim** (the outlet account's username), as in the revised spec. There is no `outlet_session` claim.
5. **The `OutletRequirement` hq-admin bypass becomes opt-in per policy** (deferred finding from Plan 1's review). `OutletSell` does not bypass.

## Review Focus

1. **Wrong PINs on one till count toward the same cashier's lockout on every till.** After 5 failures anywhere, the right PIN gets `pin_locked` on another till too. Test in Task 4.
2. **A till account is released or deactivated while the till still holds an unexpired outlet access token.** The next cashier PIN sign-in on that till fails with `outlet_session_invalid`. Tests in Task 4 (release) and Task 7 (deactivate).
3. **An empty PIN is submitted** (an accidental Enter on the PIN pad). Expected: `pin_missing`, not counted toward lockout. Test in Task 4.
4. **The same cashier signs in on two tills of one outlet.** Both work, and each receipt names its own till. Test in Task 6.
5. **Two tills are added to the same outlet in a row.** Expected: distinct, increasing account names, never a duplicate or a 500. Test in Task 7.

---

## File Structure

```
keycloak/
  realm/lantern-realm.json                      # Tasks 1,3,4,5,7 patch it with the scripts given
  pin-authenticator/src/main/java/dev/lantern/keycloak/
    PinRules.java                               # Task 2: rules for cashier-chosen PINs (pure)
    TillErrors.java                             # Task 2: error codes + remaining-attempts format (pure)
    CashierRoles.java                           # Task 3: realm role names the extensions use
    PinCredentials.java                         # Task 3: hash/store/check lantern-pin credentials
    PinAdminResource.java                       # Task 3: PUT /realms/{realm}/lantern-pin/users/{id}
    PinAdminResourceProviderFactory.java        # Task 3
    OutletSessionCheckAuthenticator.java        # Task 4: step 1 of till-cashier-pin
    CashierCheckAuthenticator.java              # Task 4: step 2
    PinCheckAuthenticator.java                  # Task 4: step 3
    TillAccountLimitAuthenticator.java          # Task 5: one live till login per account
  pin-authenticator/src/main/resources/META-INF/services/
    org.keycloak.authentication.AuthenticatorFactory          # Tasks 4–5
    org.keycloak.services.resource.RealmResourceProviderFactory  # Task 3
  pin-authenticator/src/test/java/dev/lantern/keycloak/
    PinRulesTest.java  TillErrorsTest.java      # Task 2
src/Lantern.Api/
  Auth/OutletRequirement.cs                     # Task 6: AllowedRoles + HqAdminBypass
  Auth/Policies.cs                              # Task 6: OutletSell, OutletReceipts
  Data/DemoStore.cs                             # Task 6: catalog, sales, receipts
  Data/Outlets.cs                               # Task 7: outlet id ↔ group path, slug, cashier digit
  Endpoints/SalesEndpoints.cs                   # Task 6
  Endpoints/TillEndpoints.cs                    # Task 7
  Endpoints/CashierEndpoints.cs                 # Task 8
  Keycloak/KeycloakModels.cs                    # Tasks 7–8: KcClient, KcUserSession, TillAccount, TillSession, UserCreation
  Keycloak/KeycloakAdminClient.cs               # Tasks 7–8: tills, cashiers, PINs
  Keycloak/PinGenerator.cs                      # Task 8
tests/Lantern.IntegrationTests/
  Infrastructure/OidcBrowser.cs                 # Task 1: programmatic browser login + logout
  Infrastructure/TillModels.cs                  # Tasks 1,4: TokenSet, LoginOutcome, OpenTill
  Infrastructure/Jwt.cs                         # Task 1: decode payloads in tests
  Infrastructure/KeycloakFixture.cs             # Tasks 1,3,4,6: till/cashier helpers
  OfflineOutletLoginTests.cs                    # Task 1
  PinAdminTests.cs                              # Task 3
  CashierPinFlowTests.cs                        # Task 4
  TillAccountLimitTests.cs                      # Task 5
  SalesTests.cs                                 # Task 6
  TillAccountTests.cs                           # Task 7
  CashierManagementTests.cs                     # Task 8
```

---

### Task 1: Persistent outlet login with offline tokens (spike for spec risk §12.3)

**Files:**
- Create: `tests/Lantern.IntegrationTests/Infrastructure/OidcBrowser.cs`, `Infrastructure/TillModels.cs`, `Infrastructure/Jwt.cs`
- Modify: `tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`, `keycloak/realm/lantern-realm.json`
- Test: `tests/Lantern.IntegrationTests/OfflineOutletLoginTests.cs`

**Interfaces:**
- Consumes: Plan 1 fixture (`Issuer`, `Http`, `AdminClientAsync`, `CreateTempUserAsync`, `DemoPassword`).
- Produces:
  - `OidcBrowser(string issuer)` with `LoginAsync(username, password, scope = "openid offline_access")` → `LoginOutcome`, and `EndOnlineSessionAsync(string idToken)`. Constants: `TillClientId`, `TillClientSecret`, `TillRedirectUri`, `TillPostLogoutUri`. Static `Snippet(string html)`.
  - `TokenSet(string AccessToken, string RefreshToken, string IdToken)`.
  - `LoginOutcome(TokenSet? Tokens, int Status, string? Html)` with `Succeeded`.
  - `Jwt.Payload(string token)` → `JsonNode`.
  - Fixture methods:
    - `CreateTempTillAsync(string outletGroupPath)` → `(string Id, string Username)`
    - `OutletLoginAsync(string username, string password = DemoPassword, bool endOnlineSession = true)` → `TokenSet`
    - `RefreshTillTokenAsync(string refreshToken)` → `(HttpStatusCode Status, JsonNode Body)`
    - `GetTillClientUuidAsync()` → `string`
    - `RevokeTillLoginAsync(string userId)`
  - Realm: `outlet-device` is a composite that includes `offline_access`.

- [ ] **Step 1: Create the branch**

```bash
cd ~/projects/Personal/DotNet/lantern-auth
git checkout -b irfan/till-backend irfan/design-spec
```

- [ ] **Step 2: Write the test infrastructure**

`tests/Lantern.IntegrationTests/Infrastructure/TillModels.cs`:
```csharp
namespace Lantern.IntegrationTests.Infrastructure;

public sealed record TokenSet(string AccessToken, string RefreshToken, string IdToken);

public sealed record LoginOutcome(TokenSet? Tokens, int Status, string? Html)
{
    public bool Succeeded => Tokens is not null;
    public static LoginOutcome Success(TokenSet tokens) => new(tokens, 200, null);
    public static LoginOutcome Refused(int status, string html) => new(null, status, html);
}
```

`tests/Lantern.IntegrationTests/Infrastructure/Jwt.cs`:
```csharp
using System.Text.Json.Nodes;

namespace Lantern.IntegrationTests.Infrastructure;

public static class Jwt
{
    public static JsonNode Payload(string token)
    {
        var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonNode.Parse(Convert.FromBase64String(part))!;
    }
}
```

`tests/Lantern.IntegrationTests/Infrastructure/OidcBrowser.cs`:
```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// Drives Keycloak's browser login for the till client over plain HTTP: authorization code + PKCE,
/// with a cookie jar, the way a till's browser and server would together.
/// </summary>
public sealed partial class OidcBrowser(string issuer) : IDisposable
{
    public const string TillClientId = "till";
    public const string TillClientSecret = "till-dev-secret";
    public const string TillRedirectUri = "http://localhost:5400/signin-oidc";
    public const string TillPostLogoutUri = "http://localhost:5400/signed-out";

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        CookieContainer = new CookieContainer(),
        AllowAutoRedirect = false
    });

    [GeneratedRegex("<form\\b[^>]*\\bid=\"kc-form-login\"[^>]*>")]
    private static partial Regex LoginFormTag();

    [GeneratedRegex("\\baction=\"([^\"]+)\"")]
    private static partial Regex ActionAttribute();

    public async Task<LoginOutcome> LoginAsync(string username, string password, string scope = "openid offline_access")
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = $"{issuer}/protocol/openid-connect/auth?client_id={TillClientId}&response_type=code" +
                        $"&redirect_uri={Uri.EscapeDataString(TillRedirectUri)}&scope={Uri.EscapeDataString(scope)}" +
                        $"&state={Guid.NewGuid():N}&code_challenge={challenge}&code_challenge_method=S256";

        using var page = await _http.GetAsync(authorize);
        string? redirect;
        if (IsRedirectToTill(page, out redirect))
            return await ExchangeAsync(redirect!, verifier); // SSO cookie skipped the form

        var html = await page.Content.ReadAsStringAsync();
        var action = FindLoginAction(html)
                     ?? throw new InvalidOperationException($"No login form ({(int)page.StatusCode}): {Snippet(html)}");

        using var submitted = await _http.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["credentialId"] = ""
        }));
        if (!IsRedirectToTill(submitted, out redirect))
            return LoginOutcome.Refused((int)submitted.StatusCode, await submitted.Content.ReadAsStringAsync());

        return await ExchangeAsync(redirect!, verifier);
    }

    /// <summary>RP-initiated logout of the browser's online session, as the till does right after registering.</summary>
    public async Task EndOnlineSessionAsync(string idToken)
    {
        using var response = await _http.GetAsync(
            $"{issuer}/protocol/openid-connect/logout?id_token_hint={Uri.EscapeDataString(idToken)}" +
            $"&post_logout_redirect_uri={Uri.EscapeDataString(TillPostLogoutUri)}");
        if (response.StatusCode != HttpStatusCode.Found)
            throw new InvalidOperationException($"Logout did not redirect ({(int)response.StatusCode}): {Snippet(await response.Content.ReadAsStringAsync())}");
    }

    public static string Snippet(string html)
    {
        var text = Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), "\\s+", " ").Trim();
        return text.Length > 300 ? text[..300] : text;
    }

    public void Dispose() => _http.Dispose();

    private async Task<LoginOutcome> ExchangeAsync(string redirect, string verifier)
    {
        var code = HttpUtility.ParseQueryString(new Uri(redirect).Query)["code"]
                   ?? throw new InvalidOperationException($"No code in redirect: {redirect}");
        using var response = await _http.PostAsync($"{issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = TillRedirectUri,
                ["client_id"] = TillClientId,
                ["client_secret"] = TillClientSecret,
                ["code_verifier"] = verifier
            }));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Code exchange failed ({(int)response.StatusCode}): {body}");
        var json = JsonNode.Parse(body)!;
        return LoginOutcome.Success(new TokenSet((string)json["access_token"]!, (string)json["refresh_token"]!, (string)json["id_token"]!));
    }

    private static bool IsRedirectToTill(HttpResponseMessage response, out string? location)
    {
        location = response.Headers.Location?.ToString();
        return response.StatusCode == HttpStatusCode.Found && location is not null &&
               location.StartsWith(TillRedirectUri, StringComparison.Ordinal);
    }

    private static string? FindLoginAction(string html)
    {
        var tag = LoginFormTag().Match(html);
        if (!tag.Success) return null;
        var action = ActionAttribute().Match(tag.Value);
        return action.Success ? WebUtility.HtmlDecode(action.Groups[1].Value) : null;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

`tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`: add these members after `LogoutUserAsync`:
```csharp
    public async Task<(string Id, string Username)> CreateTempTillAsync(string outletGroupPath)
    {
        var (id, username, _) = await CreateTempUserAsync(outletGroupPath);
        using var admin = await AdminClientAsync();
        var role = await admin.GetFromJsonAsync<JsonObject>("roles/outlet-device");
        using var map = await admin.PostAsJsonAsync($"users/{id}/role-mappings/realm", new[] { role });
        map.EnsureSuccessStatusCode();
        return (id, username);
    }

    /// <summary>Signs a till in as an outlet account; like the till, then ends the online session.</summary>
    public async Task<TokenSet> OutletLoginAsync(string username, string password = DemoPassword, bool endOnlineSession = true)
    {
        using var browser = new OidcBrowser(Issuer);
        var outcome = await browser.LoginAsync(username, password);
        if (!outcome.Succeeded)
            throw new InvalidOperationException($"Outlet login for {username} refused ({outcome.Status}): {OidcBrowser.Snippet(outcome.Html!)}");
        if (endOnlineSession) await browser.EndOnlineSessionAsync(outcome.Tokens!.IdToken);
        return outcome.Tokens!;
    }

    public async Task<(HttpStatusCode Status, JsonNode Body)> RefreshTillTokenAsync(string refreshToken)
    {
        using var response = await Http.PostAsync($"{Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = OidcBrowser.TillClientId,
                ["client_secret"] = OidcBrowser.TillClientSecret
            }));
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    public async Task<string> GetTillClientUuidAsync()
    {
        using var admin = await AdminClientAsync();
        var clients = await admin.GetFromJsonAsync<JsonElement>("clients?clientId=till");
        return clients.EnumerateArray().Single().GetProperty("id").GetString()!;
    }

    public async Task RevokeTillLoginAsync(string userId)
    {
        using var admin = await AdminClientAsync();
        using var response = await admin.DeleteAsync($"users/{userId}/consents/till");
        response.EnsureSuccessStatusCode();
    }
```
Also add `using System.Net;` to the fixture's usings.

- [ ] **Step 3: Write the failing tests**

`tests/Lantern.IntegrationTests/OfflineOutletLoginTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.1 and risk §12.3: the outlet login is an offline session that outlives the browser session.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class OfflineOutletLoginTests(KeycloakFixture kc)
{
    [Fact]
    public async Task Outlet_login_returns_an_offline_refresh_token()
    {
        var (_, till) = await kc.CreateTempTillAsync("/Outlets/Bangsar");

        var tokens = await kc.OutletLoginAsync(till);

        Assert.Equal("Offline", (string?)Jwt.Payload(tokens.RefreshToken)["typ"]);
    }

    [Fact]
    public async Task Offline_login_survives_ending_the_online_session()
    {
        var (_, till) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        var tokens = await kc.OutletLoginAsync(till, endOnlineSession: true);

        var (status, body) = await kc.RefreshTillTokenAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(string.IsNullOrEmpty((string?)body["access_token"]));
    }

    [Fact]
    public async Task Refreshed_access_token_names_the_offline_session()
    {
        var (id, till) = await kc.CreateTempTillAsync("/Outlets/KLCC");
        var tokens = await kc.OutletLoginAsync(till);
        var (_, body) = await kc.RefreshTillTokenAsync(tokens.RefreshToken);
        var sid = (string?)Jwt.Payload((string)body["access_token"]!)["sid"];

        using var admin = await kc.AdminClientAsync();
        var sessions = await admin.GetFromJsonAsync<JsonElement>($"users/{id}/offline-sessions/{await kc.GetTillClientUuidAsync()}");

        Assert.False(string.IsNullOrEmpty(sid));
        Assert.Contains(sid, sessions.EnumerateArray().Select(s => s.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Revoking_the_till_consent_ends_the_outlet_login()
    {
        var (id, till) = await kc.CreateTempTillAsync("/Outlets/PJ");
        var tokens = await kc.OutletLoginAsync(till);

        await kc.RevokeTillLoginAsync(id);
        var (status, body) = await kc.RefreshTillTokenAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid_grant", (string?)body["error"]);
    }
}
```

- [ ] **Step 4: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~OfflineOutletLoginTests`
Expected: FAIL. Temporary till accounts don't have the `offline_access` role, so the code exchange either errors ("Offline tokens not allowed…") or returns a normal `Refresh` token instead of `Offline`. If the failure is instead "No login form" or "Code exchange failed: … PKCE", fix the helper first. That failure means the helper doesn't work, not that the feature is missing.

- [ ] **Step 5: Make `outlet-device` grant `offline_access`, and reformat the realm file once**

Keycloak creates `offline_access` before importing realm roles, so the composite resolves. Don't add `offline_access` to the roles list: Keycloak strips it from the list during import anyway.

```bash
python3 - <<'EOF'
import json, pathlib
p = pathlib.Path("keycloak/realm/lantern-realm.json")
realm = json.loads(p.read_text())
role = next(r for r in realm["roles"]["realm"] if r["name"] == "outlet-device")
role["composite"] = True
role["composites"] = {"realm": ["offline_access"]}
p.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n")
EOF
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~OfflineOutletLoginTests`
Expected: PASS (4 tests). This retires spec risk §12.3:
- ending the online session leaves the offline session alive
- the refreshed token's `sid` is the offline session
- revoking consent ends it

If `Refreshed_access_token_names_the_offline_session` fails because `sid` is missing, stop and record a ruling: the plugin's outlet check in Task 4 would then have to identify the till login another way.

- [ ] **Step 7: Commit**

```bash
git add keycloak/realm/lantern-realm.json tests
git commit -m "feat: outlet logins as offline sessions with browser-login test harness"
```

---

### Task 2: PIN rules and till error codes (pure Java, unit tested)

**Files:**
- Create: `keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/PinRules.java`, `TillErrors.java`
- Test: `keycloak/pin-authenticator/src/test/java/dev/lantern/keycloak/PinRulesTest.java`, `TillErrorsTest.java`

**Interfaces:**
- Produces:
  - `PinRules.isWellFormed(String)` → true for 4–6 digits.
  - `PinRules.checkNewPin(String newPin, String temporaryPin)` → `Optional<String>` error code.
  - `TillErrors` constants (the codes in Global Constraints) and `TillErrors.pinInvalid(int failureFactor, int failuresSoFar)` → `"pin_invalid:<n>"`.

- [ ] **Step 1: Write the failing tests**

`keycloak/pin-authenticator/src/test/java/dev/lantern/keycloak/PinRulesTest.java`:
```java
package dev.lantern.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.Optional;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.junit.jupiter.params.provider.ValueSource;

class PinRulesTest {

    @ParameterizedTest
    @ValueSource(strings = {"1111", "2580", "135790"})
    void fourToSixDigitsAreWellFormed(String pin) {
        assertTrue(PinRules.isWellFormed(pin));
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "123", "1234567", "12a4", " 1234", "12 34", "١٢٣٤"})
    void anythingElseIsNot(String pin) {
        assertFalse(PinRules.isWellFormed(pin));
    }

    @ParameterizedTest
    @CsvSource({
            "2580, 864213, ",
            "135790, 864213, ",
            "12a4, 864213, pin_rule_format",
            "864213, 864213, pin_rule_same_as_temporary",
            "0000, 864213, pin_rule_repeated_digit",
            "1234, 864213, pin_rule_sequence",
            "98765, 864213, pin_rule_sequence"
    })
    void newPinRules(String newPin, String temporaryPin, String expected) {
        assertEquals(Optional.ofNullable(expected), PinRules.checkNewPin(newPin, temporaryPin));
    }
}
```

`keycloak/pin-authenticator/src/test/java/dev/lantern/keycloak/TillErrorsTest.java`:
```java
package dev.lantern.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

class TillErrorsTest {

    @ParameterizedTest
    @CsvSource({"5, 0, pin_invalid:4", "5, 3, pin_invalid:1", "5, 4, pin_invalid:0", "5, 9, pin_invalid:0"})
    void pinInvalidCarriesRemainingAttempts(int failureFactor, int failuresSoFar, String expected) {
        assertEquals(expected, TillErrors.pinInvalid(failureFactor, failuresSoFar));
    }
}
```

`junit-jupiter` already includes the params module, so the pom needs no change.

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
docker run --rm -v lantern-m2:/root/.m2 -v "$PWD/keycloak/pin-authenticator":/build -w /build \
  maven:3.9-eclipse-temurin-21 mvn -q -B test
```
Expected: FAIL. Compilation fails with `cannot find symbol: class PinRules` and `class TillErrors`.

- [ ] **Step 3: Implement**

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/PinRules.java`:
```java
package dev.lantern.keycloak;

import java.util.Optional;
import java.util.regex.Pattern;

/** Rules for PINs a cashier chooses (spec §6.4). Pure functions so they can be unit-tested. */
public final class PinRules {

    private static final Pattern WELL_FORMED = Pattern.compile("[0-9]{4,6}");

    private PinRules() {
    }

    public static boolean isWellFormed(String pin) {
        return pin != null && WELL_FORMED.matcher(pin).matches();
    }

    /** @return the error code when {@code newPin} is not acceptable, empty when it is. */
    public static Optional<String> checkNewPin(String newPin, String temporaryPin) {
        if (!isWellFormed(newPin)) return Optional.of("pin_rule_format");
        if (newPin.equals(temporaryPin)) return Optional.of("pin_rule_same_as_temporary");
        if (newPin.chars().distinct().count() == 1) return Optional.of("pin_rule_repeated_digit");
        if (isSimpleRun(newPin)) return Optional.of("pin_rule_sequence");
        return Optional.empty();
    }

    private static boolean isSimpleRun(String pin) {
        boolean up = true;
        boolean down = true;
        for (int i = 1; i < pin.length(); i++) {
            int step = pin.charAt(i) - pin.charAt(i - 1);
            up &= step == 1;
            down &= step == -1;
        }
        return up || down;
    }
}
```

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/TillErrors.java`:
```java
package dev.lantern.keycloak;

/** error_description codes the till maps to messages (spec §7). */
public final class TillErrors {

    public static final String OUTLET_SESSION_INVALID = "outlet_session_invalid";
    public static final String CASHIER_NOT_FOUND = "cashier_not_found";
    public static final String CASHIER_WRONG_OUTLET = "cashier_wrong_outlet";
    public static final String CASHIER_DISABLED = "cashier_disabled";
    public static final String PIN_MISSING = "pin_missing";
    public static final String PIN_INVALID = "pin_invalid";
    public static final String PIN_LOCKED = "pin_locked";
    public static final String PIN_CHANGE_REQUIRED = "pin_change_required";

    private TillErrors() {
    }

    /** {@code pin_invalid:<remaining attempts before lockout>}, never negative. */
    public static String pinInvalid(int failureFactor, int failuresSoFar) {
        return PIN_INVALID + ":" + Math.max(failureFactor - failuresSoFar - 1, 0);
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run the same `docker run … mvn -q -B test` command as Step 2.
Expected: exit code 0. Without `-q`, the summary reads `Tests run: 25, Failures: 0`: 4 mapper + 3 + 7 + 7 rules + 4 errors, with parameterized cases counted individually.

- [ ] **Step 5: Commit**

```bash
git add keycloak/pin-authenticator
git commit -m "feat: pin rules and till error codes"
```

---

### Task 3: PIN storage and the admin PIN resource, with seeded demo PINs

**Files:**
- Create: `keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/CashierRoles.java`, `PinCredentials.java`, `PinAdminResource.java`, `PinAdminResourceProviderFactory.java`
- Create: `keycloak/pin-authenticator/src/main/resources/META-INF/services/org.keycloak.services.resource.RealmResourceProviderFactory`
- Modify: `keycloak/realm/lantern-realm.json`, `tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`
- Test: `tests/Lantern.IntegrationTests/PinAdminTests.cs`

**Interfaces:**
- Consumes: `PinRules.isWellFormed` (Task 2).
- Produces:
  - `PinCredentials.TYPE = "lantern-pin"`.
  - `PinCredentials.set(KeycloakSession, UserModel, String pin, boolean temporary)` replaces any existing PIN.
  - `PinCredentials.check(KeycloakSession, UserModel, String pin)` → `Check { NO_PIN, MISMATCH, OK, OK_TEMPORARY }`.
  - REST: `PUT {issuer}/lantern-pin/users/{id}` with body `{"pin":"123456","temporary":true}`:
    - 204 on success; it also clears the cashier's brute-force lockout
    - 400 `{"error":"not_a_cashier"|"pin_rule_format"}`
    - 401 without a valid bearer token
    - 403 without `realm-management/manage-users`
    - 404 `{"error":"user_not_found"}`
  - Fixture:
    - `ServiceTokenAsync()` → `string` (api-admin-svc token)
    - `SetPinAsync(string userId, string pin, bool temporary)`
    - `CreateTempCashierAsync(string outletGroupPath, string? pin = null, bool temporary = false)` → `(string Id, string Username)`
  - Realm: seeded cashiers carry `lantern-pin` credentials, and `c-3001`'s PIN is temporary.

- [ ] **Step 1: Write the fixture helpers and the failing tests**

`tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`: add after `RevokeTillLoginAsync`:
```csharp
    public async Task<string> ServiceTokenAsync()
    {
        using var response = await Http.PostAsync($"{Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "api-admin-svc",
                ["client_secret"] = "api-admin-svc-dev-secret"
            }));
        response.EnsureSuccessStatusCode();
        return (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["access_token"]!;
    }

    public async Task SetPinAsync(string userId, string pin, bool temporary)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Issuer}/lantern-pin/users/{userId}")
        {
            Content = JsonContent.Create(new { pin, temporary })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await ServiceTokenAsync());
        using var response = await Http.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Setting PIN failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    public async Task<(string Id, string Username)> CreateTempCashierAsync(string outletGroupPath, string? pin = null, bool temporary = false)
    {
        var (id, username, _) = await CreateTempUserAsync(outletGroupPath);
        using var admin = await AdminClientAsync();
        var role = await admin.GetFromJsonAsync<JsonObject>("roles/cashier");
        using var map = await admin.PostAsJsonAsync($"users/{id}/role-mappings/realm", new[] { role });
        map.EnsureSuccessStatusCode();
        if (pin is not null) await SetPinAsync(id, pin, temporary);
        return (id, username);
    }
```

`tests/Lantern.IntegrationTests/PinAdminTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class PinAdminTests(KeycloakFixture kc)
{
    private async Task<HttpResponseMessage> PutPinAsync(string userId, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{kc.Issuer}/lantern-pin/users/{userId}")
        {
            Content = JsonContent.Create(body)
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await kc.Http.SendAsync(request);
    }

    private async Task<JsonElement[]> PinCredentialsOf(string userId)
    {
        using var admin = await kc.AdminClientAsync();
        var creds = await admin.GetFromJsonAsync<JsonElement>($"users/{userId}/credentials");
        return creds.EnumerateArray().Where(c => c.GetProperty("type").GetString() == "lantern-pin").ToArray();
    }

    [Fact]
    public async Task Setting_a_temporary_pin_stores_one_lantern_pin_credential()
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar");

        var response = await PutPinAsync(id, new { pin = "864213", temporary = true }, await kc.ServiceTokenAsync());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var pin = Assert.Single(await PinCredentialsOf(id));
        Assert.Equal("PIN (temporary)", pin.GetProperty("userLabel").GetString());
    }

    [Fact]
    public async Task Replacing_a_pin_keeps_a_single_credential()
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "864213", temporary: true);

        await PutPinAsync(id, new { pin = "2580", temporary = false }, await kc.ServiceTokenAsync());

        var pin = Assert.Single(await PinCredentialsOf(id));
        Assert.Equal("PIN", pin.GetProperty("userLabel").GetString());
    }

    [Fact]
    public async Task Seeded_cashiers_have_pins_and_c3001s_is_temporary()
    {
        var labels = new Dictionary<string, string?>();
        foreach (var code in new[] { "c-1001", "c-3001" })
            labels[code] = Assert.Single(await PinCredentialsOf(await kc.GetUserIdAsync(code))).GetProperty("userLabel").GetString();

        Assert.Equal("PIN", labels["c-1001"]);
        Assert.Equal("PIN (temporary)", labels["c-3001"]);
    }

    [Fact]
    public async Task Non_cashier_is_400()
    {
        var response = await PutPinAsync(await kc.GetUserIdAsync("eric.procure"), new { pin = "2580" }, await kc.ServiceTokenAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("not_a_cashier", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("123")]
    [InlineData("")]
    public async Task Malformed_pin_is_400(string pin)
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar");
        var response = await PutPinAsync(id, new { pin }, await kc.ServiceTokenAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_without_manage_users_is_403()
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar");
        var response = await PutPinAsync(id, new { pin = "2580" }, await kc.GetUserTokenAsync("chloe.staff"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_token_is_401()
    {
        var (id, _) = await kc.CreateTempCashierAsync("/Outlets/Bangsar");
        var response = await PutPinAsync(id, new { pin = "2580" }, token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_user_is_404_with_reason()
    {
        var response = await PutPinAsync(Guid.NewGuid().ToString(), new { pin = "2580" }, await kc.ServiceTokenAsync());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("user_not_found", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~PinAdminTests`
Expected: FAIL. Every PUT returns 404 because the resource doesn't exist. The setup helpers in the Replacing and Malformed tests throw "Setting PIN failed: 404". The seeded test fails because there's no `lantern-pin` credential.

- [ ] **Step 3: Implement PIN storage and the resource**

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/PinCredentials.java`:
```java
package dev.lantern.keycloak;

import java.io.IOException;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.keycloak.common.util.MultivaluedHashMap;
import org.keycloak.common.util.Time;
import org.keycloak.credential.CredentialModel;
import org.keycloak.credential.hash.PasswordHashProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.UserModel;
import org.keycloak.models.credential.PasswordCredentialModel;
import org.keycloak.models.credential.dto.PasswordCredentialData;
import org.keycloak.util.JsonSerialization;

/**
 * Stores a cashier PIN as a hashed credential of type {@value #TYPE}, using the realm's password
 * hashing (spec §6.6). The temporary flag rides in the hash parameters ("algorithmData").
 */
public final class PinCredentials {

    public static final String TYPE = "lantern-pin";
    static final String TEMPORARY_FLAG = "lantern-temporary";

    public enum Check { NO_PIN, MISMATCH, OK, OK_TEMPORARY }

    private PinCredentials() {
    }

    public static void set(KeycloakSession session, UserModel user, String pin, boolean temporary) {
        PasswordHashProvider hasher = session.getProvider(PasswordHashProvider.class);
        PasswordCredentialModel hashed = hasher.encodedCredential(pin, -1);
        PasswordCredentialData data = hashed.getPasswordCredentialData();

        Map<String, List<String>> params = new HashMap<>();
        if (data.getAdditionalParameters() != null) params.putAll(data.getAdditionalParameters());
        if (temporary) params.put(TEMPORARY_FLAG, List.of("true"));

        CredentialModel credential = new CredentialModel();
        credential.setType(TYPE);
        credential.setUserLabel(temporary ? "PIN (temporary)" : "PIN");
        credential.setCreatedDate(Time.currentTimeMillis());
        credential.setSecretData(hashed.getSecretData());
        try {
            credential.setCredentialData(JsonSerialization.writeValueAsString(
                    new PasswordCredentialData(data.getHashIterations(), data.getAlgorithm(), params)));
        } catch (IOException e) {
            throw new IllegalStateException("Could not serialise the PIN credential", e);
        }

        user.credentialManager().getStoredCredentialsByTypeStream(TYPE).toList()
                .forEach(old -> user.credentialManager().removeStoredCredentialById(old.getId()));
        user.credentialManager().createStoredCredential(credential);
    }

    public static Check check(KeycloakSession session, UserModel user, String pin) {
        CredentialModel stored = user.credentialManager().getStoredCredentialsByTypeStream(TYPE).findFirst().orElse(null);
        if (stored == null) return Check.NO_PIN;

        PasswordCredentialModel model = PasswordCredentialModel.createFromCredentialModel(stored);
        PasswordHashProvider hasher = session.getProvider(PasswordHashProvider.class,
                model.getPasswordCredentialData().getAlgorithm());
        if (hasher == null || pin == null || !hasher.verify(pin, model)) return Check.MISMATCH;

        MultivaluedHashMap<String, String> params = model.getPasswordCredentialData().getAdditionalParameters();
        boolean temporary = params != null && "true".equals(params.getFirst(TEMPORARY_FLAG));
        return temporary ? Check.OK_TEMPORARY : Check.OK;
    }
}
```

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/PinAdminResource.java`:
```java
package dev.lantern.keycloak;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.PUT;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.PathParam;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;
import java.util.Map;
import org.keycloak.models.AdminRoles;
import org.keycloak.models.Constants;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;
import org.keycloak.representations.AccessToken;
import org.keycloak.services.managers.AppAuthManager;
import org.keycloak.services.managers.AuthenticationManager;

/** PUT /realms/{realm}/lantern-pin/users/{id}: set a cashier's PIN. Caller needs realm-management/manage-users. */
public class PinAdminResource {

    public static final class PinRequest {
        public String pin;
        public Boolean temporary;
    }

    private final KeycloakSession session;

    public PinAdminResource(KeycloakSession session) {
        this.session = session;
    }

    @PUT
    @Path("users/{id}")
    @Consumes(MediaType.APPLICATION_JSON)
    public Response setPin(@PathParam("id") String id, PinRequest request) {
        AuthenticationManager.AuthResult auth = new AppAuthManager.BearerTokenAuthenticator(session).authenticate();
        if (auth == null) return Response.status(Response.Status.UNAUTHORIZED).build();
        AccessToken.Access access = auth.token().getResourceAccess(Constants.REALM_MANAGEMENT_CLIENT_ID);
        if (access == null || !access.isUserInRole(AdminRoles.MANAGE_USERS)) return Response.status(Response.Status.FORBIDDEN).build();

        RealmModel realm = session.getContext().getRealm();
        UserModel user = session.users().getUserById(realm, id);
        if (user == null) return error(Response.Status.NOT_FOUND, "user_not_found");
        RoleModel cashier = realm.getRole(CashierRoles.CASHIER);
        if (cashier == null || !user.hasRole(cashier)) return error(Response.Status.BAD_REQUEST, "not_a_cashier");
        if (request == null || !PinRules.isWellFormed(request.pin)) return error(Response.Status.BAD_REQUEST, "pin_rule_format");

        PinCredentials.set(session, user, request.pin, request.temporary == null || request.temporary);
        session.loginFailures().removeUserLoginFailure(realm, user.getId());
        return Response.noContent().build();
    }

    private static Response error(Response.Status status, String code) {
        return Response.status(status).entity(Map.of("error", code)).type(MediaType.APPLICATION_JSON_TYPE).build();
    }
}
```

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/CashierRoles.java`:
```java
package dev.lantern.keycloak;

/** Realm role names the extensions depend on (spec §4.2). */
public final class CashierRoles {
    public static final String CASHIER = "cashier";
    public static final String OUTLET_DEVICE = "outlet-device";

    private CashierRoles() {
    }
}
```

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/PinAdminResourceProviderFactory.java`:
```java
package dev.lantern.keycloak;

import org.keycloak.Config;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.services.resource.RealmResourceProvider;
import org.keycloak.services.resource.RealmResourceProviderFactory;

public class PinAdminResourceProviderFactory implements RealmResourceProviderFactory {

    public static final String ID = "lantern-pin";

    @Override
    public RealmResourceProvider create(KeycloakSession session) {
        return new RealmResourceProvider() {
            @Override
            public Object getResource() {
                return new PinAdminResource(session);
            }

            @Override
            public void close() {
            }
        };
    }

    @Override
    public void init(Config.Scope config) {
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
    }

    @Override
    public String getId() {
        return ID;
    }
}
```

`keycloak/pin-authenticator/src/main/resources/META-INF/services/org.keycloak.services.resource.RealmResourceProviderFactory`:
```
dev.lantern.keycloak.PinAdminResourceProviderFactory
```

- [ ] **Step 4: Seed the demo PINs**

These are precomputed `pbkdf2-sha512` hashes (210,000 iterations, 512-bit key, 16-byte salt) in Keycloak's stored-credential format. Import stores them as-is, and `PinCredentials.check` verifies them with the algorithm named in `credentialData`.

```bash
python3 - <<'EOF'
import json, pathlib
p = pathlib.Path("keycloak/realm/lantern-realm.json")
realm = json.loads(p.read_text())
def pin(secret, temporary=False):
    data = {"hashIterations": 210000, "algorithm": "pbkdf2-sha512",
            "algorithmData": ({"lantern-temporary": ["true"]} if temporary else {})}
    return {"type": "lantern-pin", "userLabel": "PIN (temporary)" if temporary else "PIN",
            "secretData": secret, "credentialData": json.dumps(data, separators=(",", ":"))}
seeds = {
  "c-1001": pin("{\"value\":\"3GuRmRnDpfHBmogDsg4sL8hQQO2h/kPKaDvjZeS8B9jAuFT9HH1TSLo/MzcgaY160uQPIY0bLcA6bxHXaG/l7g==\",\"salt\":\"oggpEfcgENlLeEq3BiCZVQ==\",\"algorithmData\":{}}"),
  "c-1002": pin("{\"value\":\"GBfeOrCDJmyEDVX+cUagYY/OvppAByAZ3dycNHKmyX6rQHHq2qFsPAuP/LrFg5T01hn36fcRtXpiTz9MH1DIow==\",\"salt\":\"32g+x9VobnPJjJS+TEcNdA==\",\"algorithmData\":{}}"),
  "c-2001": pin("{\"value\":\"tiPcAYt8zqV1OPmy3UznqpJu6SIRlsYtD33NNirLhgXbgZD+x3v5t90l8H5HDhB/SJFOyDKQHnj1aw7oDhOyIQ==\",\"salt\":\"3YTqcANrL/wO9V2xSRErpQ==\",\"algorithmData\":{}}"),
  "c-2002": pin("{\"value\":\"5io6nj2AHsKJ2+mZ136TEV0wo8XVjNqCCSsXV8YnbpaBD6rJDp+GpBgv0Tr1/9KI5zakDo+SM0inuBee6plXIA==\",\"salt\":\"lzkGlDv6y6FC3Ft4aojF0Q==\",\"algorithmData\":{}}"),
  "c-3001": pin("{\"value\":\"olXvoAwiN77KgMs1OwS/qtPUyVtTCI7jp8+7+NEeNS6fxC2Bjs2m5U/tPPJdbes2kuHQC17m3lepABPUG1QROA==\",\"salt\":\"62OUWG1SQFa1h+vvoQ6sgg==\",\"algorithmData\":{}}", temporary=True),
}
for user in realm["users"]:
    if user["username"] in seeds:
        user["credentials"] = [seeds[user["username"]]]
p.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n")
EOF
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~PinAdminTests`
Expected: PASS (10 tests). If `No_token_is_401` returns 500 instead, `BearerTokenAuthenticator.authenticate()` threw rather than returning null for a missing header. In that case, wrap the call in `try { … } catch (RuntimeException e) { return 401; }` and record a ruling.

- [ ] **Step 6: Commit**

```bash
git add keycloak tests
git commit -m "feat: hashed cashier pins with admin pin resource and seeded demo pins"
```

---

### Task 4: Cashier PIN sign-in (the `till-cashier-pin` direct-grant flow)

**Files:**
- Create: `keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/OutletSessionCheckAuthenticator.java`, `CashierCheckAuthenticator.java`, `PinCheckAuthenticator.java`
- Create: `keycloak/pin-authenticator/src/main/resources/META-INF/services/org.keycloak.authentication.AuthenticatorFactory`
- Modify: `keycloak/realm/lantern-realm.json`, `tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`, `tests/Lantern.IntegrationTests/Infrastructure/TillModels.cs`
- Test: `tests/Lantern.IntegrationTests/CashierPinFlowTests.cs`

**Interfaces:**
- Consumes:
  - `OutletIdMapper.resolveOutletId` (Plan 1)
  - `PinRules`, `TillErrors` (Task 2)
  - `PinCredentials`, `CashierRoles` (Task 3)
  - fixture till helpers (Task 1) and cashier helpers (Task 3)
- Produces:
  - Authenticator ids `lantern-outlet-session-check`, `lantern-cashier-check`, `lantern-pin-check`.
  - Auth notes `lantern.outlet_id` and `lantern.till_account`.
  - User-session note `till_account`, mapped to the `till_account` claim.
  - Realm flow `till-cashier-pin`, bound as the `till` client's `direct_grant` override. The `till` client has direct access grants enabled and a 900 s client session idle.
  - Cashier tokens carry `preferred_username` (code), `name`, `roles` ⊇ `cashier`, `outlet_id`, `till_account`.
  - Fixture:
    - `OpenTillAsync(string outletGroupPath)` → `OpenTill(string AccountId, string Account, string OfflineRefreshToken, string AccessToken)`
    - `CashierPinAsync(string? outletToken, string username, string? pin, string? newPin = null)` → `(HttpStatusCode Status, JsonNode Body)`
    - static `ErrorCode(JsonNode body)` → `string?`

- [ ] **Step 1: Write the fixture helpers and the failing tests**

`tests/Lantern.IntegrationTests/Infrastructure/TillModels.cs`: append:
```csharp
/// <summary>A till signed in as an outlet account, holding its offline login and a fresh access token.</summary>
public sealed record OpenTill(string AccountId, string Account, string OfflineRefreshToken, string AccessToken);
```

`tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`: add after `CreateTempCashierAsync`:
```csharp
    /// <summary>A fresh temporary till account in the outlet, signed in the way the till does it.</summary>
    public async Task<OpenTill> OpenTillAsync(string outletGroupPath)
    {
        var (id, account) = await CreateTempTillAsync(outletGroupPath);
        var tokens = await OutletLoginAsync(account);
        var (status, body) = await RefreshTillTokenAsync(tokens.RefreshToken);
        if (status != HttpStatusCode.OK) throw new InvalidOperationException($"Outlet refresh failed: {body}");
        return new OpenTill(id, account, tokens.RefreshToken, (string)body["access_token"]!);
    }

    /// <summary>The till server's PIN request: password grant on the till client, routed to till-cashier-pin.</summary>
    public async Task<(HttpStatusCode Status, JsonNode Body)> CashierPinAsync(string? outletToken, string username, string? pin, string? newPin = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = OidcBrowser.TillClientId,
            ["client_secret"] = OidcBrowser.TillClientSecret,
            ["username"] = username,
            ["scope"] = "openid"
        };
        if (outletToken is not null) form["outlet_token"] = outletToken;
        if (pin is not null) form["pin"] = pin;
        if (newPin is not null) form["new_pin"] = newPin;
        using var response = await Http.PostAsync($"{Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(form));
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    public static string? ErrorCode(JsonNode body) => (string?)body["error_description"];
```

`tests/Lantern.IntegrationTests/CashierPinFlowTests.cs`:
```csharp
using System.Net;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.2–6.4: cashier PIN sign-in at a till with a live outlet login.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class CashierPinFlowTests(KeycloakFixture kc)
{
    [Fact]
    public async Task Seeded_cashier_signs_in_and_token_names_cashier_outlet_and_till()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");

        var (status, body) = await kc.CashierPinAsync(till.AccessToken, "c-1001", "1111");

        Assert.Equal(HttpStatusCode.OK, status);
        var claims = Jwt.Payload((string)body["access_token"]!);
        Assert.Equal("c-1001", (string?)claims["preferred_username"]);
        Assert.Equal("Siti Aminah", (string?)claims["name"]);
        Assert.Equal("BGS", (string?)claims["outlet_id"]);
        Assert.Equal(till.Account, (string?)claims["till_account"]);
        Assert.Contains("cashier", claims["roles"]!.AsArray().Select(r => (string?)r));
    }

    [Fact]
    public async Task Wrong_pin_reports_remaining_attempts()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");

        var first = await kc.CashierPinAsync(till.AccessToken, cashier, "0000");
        var second = await kc.CashierPinAsync(till.AccessToken, cashier, "0000");

        Assert.Equal(HttpStatusCode.BadRequest, first.Status);
        Assert.Equal("pin_invalid:4", KeycloakFixture.ErrorCode(first.Body));
        Assert.Equal("pin_invalid:3", KeycloakFixture.ErrorCode(second.Body));
    }

    [Fact]
    public async Task Five_wrong_pins_on_one_till_lock_the_cashier_on_another_till()
    {
        var tillA = await kc.OpenTillAsync("/Outlets/Bangsar");
        var tillB = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        for (var i = 0; i < 5; i++) await kc.CashierPinAsync(tillA.AccessToken, cashier, "0000");

        var (status, body) = await kc.CashierPinAsync(tillB.AccessToken, cashier, "2468");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("pin_locked", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Empty_pin_is_missing_and_not_counted()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");

        var empty = await kc.CashierPinAsync(till.AccessToken, cashier, "");
        var wrong = await kc.CashierPinAsync(till.AccessToken, cashier, "0000");

        Assert.Equal("pin_missing", KeycloakFixture.ErrorCode(empty.Body));
        Assert.Equal("pin_invalid:4", KeycloakFixture.ErrorCode(wrong.Body));
    }

    [Fact]
    public async Task Cashier_from_another_outlet_is_refused()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, "c-2001", "3333");
        Assert.Equal("cashier_wrong_outlet", KeycloakFixture.ErrorCode(body));
    }

    [Theory]
    [InlineData("c-9999")]
    [InlineData("eric.procure")]
    [InlineData("")]
    public async Task Unknown_or_non_cashier_user_is_not_found(string username)
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, username, "1111");
        Assert.Equal("cashier_not_found", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Disabled_cashier_is_refused()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (id, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        await kc.SetUserEnabledAsync(id, false);

        var (_, body) = await kc.CashierPinAsync(till.AccessToken, cashier, "2468");

        Assert.Equal("cashier_disabled", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Pin_without_a_valid_outlet_login_is_refused()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var hqToken = await kc.GetUserTokenAsync("eric.procure");

        var missing = await kc.CashierPinAsync(null, "c-1001", "1111");
        var garbage = await kc.CashierPinAsync("not-a-token", "c-1001", "1111");
        var refreshTokenInstead = await kc.CashierPinAsync(till.OfflineRefreshToken, "c-1001", "1111");
        var notADevice = await kc.CashierPinAsync(hqToken, "c-1001", "1111");

        Assert.All(new[] { missing, garbage, refreshTokenInstead, notADevice }, r =>
            Assert.Equal("outlet_session_invalid", KeycloakFixture.ErrorCode(r.Body)));
    }

    [Fact]
    public async Task Released_till_cannot_sign_cashiers_in_with_its_unexpired_token()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        Assert.Equal(HttpStatusCode.OK, (await kc.CashierPinAsync(till.AccessToken, "c-1002", "2222")).Status);

        await kc.RevokeTillLoginAsync(till.AccountId);
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, "c-1002", "2222");

        Assert.Equal("outlet_session_invalid", KeycloakFixture.ErrorCode(body));
    }

    [Fact]
    public async Task Till_client_does_not_accept_plain_passwords()
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = OidcBrowser.TillClientId,
            ["client_secret"] = OidcBrowser.TillClientSecret, ["username"] = "eric.procure",
            ["password"] = KeycloakFixture.DemoPassword
        };
        using var response = await kc.Http.PostAsync($"{kc.Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Temporary_pin_must_be_replaced_by_an_acceptable_pin()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "864213", temporary: true);

        Assert.Equal("pin_change_required", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, cashier, "864213")).Body));
        Assert.Equal("pin_rule_sequence", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, cashier, "864213", "1234")).Body));
        Assert.Equal("pin_rule_same_as_temporary", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, cashier, "864213", "864213")).Body));

        var changed = await kc.CashierPinAsync(till.AccessToken, cashier, "864213", "2580");
        Assert.Equal(HttpStatusCode.OK, changed.Status);

        Assert.StartsWith("pin_invalid", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, cashier, "864213")).Body));
        Assert.Equal(HttpStatusCode.OK, (await kc.CashierPinAsync(till.AccessToken, cashier, "2580")).Status);
    }

    [Fact]
    public async Task Seeded_temporary_cashier_is_asked_to_change_pin()
    {
        var till = await kc.OpenTillAsync("/Outlets/PJ");
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, "c-3001", "5555");
        Assert.Equal("pin_change_required", KeycloakFixture.ErrorCode(body));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~CashierPinFlowTests`
Expected: FAIL. Every PIN request gets 400 `unauthorized_client` ("Client not allowed for direct access grants"), so error codes come back null. `Till_client_does_not_accept_plain_passwords` passes already. It stays as a guard that the PIN flow never opens a password path.

- [ ] **Step 3: Implement the three steps**

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/OutletSessionCheckAuthenticator.java`:
```java
package dev.lantern.keycloak;

import jakarta.ws.rs.core.Response;
import java.util.LinkedList;
import java.util.List;
import java.util.Optional;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.directgrant.AbstractDirectGrantAuthenticator;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.UserSessionModel;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.representations.AccessToken;
import org.keycloak.services.Urls;
import org.keycloak.util.TokenUtil;

/** Step 1 of till-cashier-pin: the request must carry a live outlet login for this client (spec §6.2). */
public class OutletSessionCheckAuthenticator extends AbstractDirectGrantAuthenticator {

    public static final String PROVIDER_ID = "lantern-outlet-session-check";
    static final String NOTE_OUTLET_ID = "lantern.outlet_id";
    static final String NOTE_TILL_ACCOUNT = "lantern.till_account";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        KeycloakSession session = context.getSession();
        RealmModel realm = context.getRealm();
        String raw = context.getHttpRequest().getDecodedFormParameters().getFirst("outlet_token");

        // decode() verifies the signature only; expiry, issuer and type are ours to check.
        AccessToken token = raw == null ? null : session.tokens().decode(raw, AccessToken.class);
        String issuer = Urls.realmIssuer(context.getUriInfo().getBaseUri(), realm.getName());
        if (token == null || !token.isActive() || !issuer.equals(token.getIssuer())
                || !TokenUtil.TOKEN_TYPE_BEARER.equals(token.getType()) || token.getSessionId() == null) {
            fail(context);
            return;
        }

        UserModel device = session.users().getUserById(realm, token.getSubject());
        RoleModel deviceRole = realm.getRole(CashierRoles.OUTLET_DEVICE);
        if (device == null || !device.isEnabled() || deviceRole == null || !device.hasRole(deviceRole)) {
            fail(context);
            return;
        }

        ClientModel till = context.getAuthenticationSession().getClient();
        UserSessionModel outletLogin = session.sessions().getOfflineUserSession(realm, token.getSessionId());
        if (outletLogin == null || !outletLogin.getUser().getId().equals(device.getId())
                || outletLogin.getAuthenticatedClientSessionByClient(till.getId()) == null) {
            fail(context);
            return;
        }

        Optional<String> outletId = OutletIdMapper.resolveOutletId(device.getGroupsStream());
        if (outletId.isEmpty()) {
            fail(context);
            return;
        }

        context.getAuthenticationSession().setAuthNote(NOTE_OUTLET_ID, outletId.get());
        context.getAuthenticationSession().setAuthNote(NOTE_TILL_ACCOUNT, device.getUsername());
        context.success();
    }

    private void fail(AuthenticationFlowContext context) {
        context.getEvent().error(TillErrors.OUTLET_SESSION_INVALID);
        context.failure(AuthenticationFlowError.INVALID_CLIENT_SESSION,
                errorResponse(Response.Status.BAD_REQUEST.getStatusCode(), "invalid_grant", TillErrors.OUTLET_SESSION_INVALID));
    }

    @Override
    public boolean requiresUser() {
        return false;
    }

    @Override
    public boolean configuredFor(KeycloakSession session, RealmModel realm, UserModel user) {
        return true;
    }

    @Override
    public void setRequiredActions(KeycloakSession session, RealmModel realm, UserModel user) {
    }

    @Override
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public String getDisplayType() {
        return "Lantern: outlet session check";
    }

    @Override
    public String getReferenceCategory() {
        return null;
    }

    @Override
    public boolean isConfigurable() {
        return false;
    }

    @Override
    public AuthenticationExecutionModel.Requirement[] getRequirementChoices() {
        return REQUIREMENT_CHOICES;
    }

    @Override
    public String getHelpText() {
        return "Requires an 'outlet_token' parameter: a live access token of an outlet-device account signed in to this client.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return new LinkedList<>();
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }
}
```

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/CashierCheckAuthenticator.java`:
```java
package dev.lantern.keycloak;

import jakarta.ws.rs.core.Response;
import java.util.LinkedList;
import java.util.List;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.directgrant.AbstractDirectGrantAuthenticator;
import org.keycloak.events.Errors;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Step 2 of till-cashier-pin: the code names an enabled cashier of the till's outlet (spec §6.2).
 * Fails before setting the user, so these failures never count toward anyone's lockout.
 */
public class CashierCheckAuthenticator extends AbstractDirectGrantAuthenticator {

    public static final String PROVIDER_ID = "lantern-cashier-check";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        RealmModel realm = context.getRealm();
        String username = context.getHttpRequest().getDecodedFormParameters().getFirst("username");
        UserModel cashier = username == null || username.isBlank()
                ? null
                : context.getSession().users().getUserByUsername(realm, username.trim());

        RoleModel cashierRole = realm.getRole(CashierRoles.CASHIER);
        if (cashier == null || cashierRole == null || !cashier.hasRole(cashierRole)) {
            fail(context, Errors.USER_NOT_FOUND, TillErrors.CASHIER_NOT_FOUND);
            return;
        }
        if (!cashier.isEnabled()) {
            fail(context, Errors.USER_DISABLED, TillErrors.CASHIER_DISABLED);
            return;
        }

        String tillOutlet = context.getAuthenticationSession().getAuthNote(OutletSessionCheckAuthenticator.NOTE_OUTLET_ID);
        String cashierOutlet = OutletIdMapper.resolveOutletId(cashier.getGroupsStream()).orElse(null);
        if (tillOutlet == null || !tillOutlet.equals(cashierOutlet)) {
            fail(context, Errors.ACCESS_DENIED, TillErrors.CASHIER_WRONG_OUTLET);
            return;
        }

        context.getAuthenticationSession().setUserSessionNote("till_account",
                context.getAuthenticationSession().getAuthNote(OutletSessionCheckAuthenticator.NOTE_TILL_ACCOUNT));
        context.setUser(cashier);
        context.success();
    }

    private void fail(AuthenticationFlowContext context, String event, String code) {
        context.getEvent().error(event);
        context.failure(AuthenticationFlowError.INVALID_USER,
                errorResponse(Response.Status.BAD_REQUEST.getStatusCode(), "invalid_grant", code));
    }

    @Override
    public boolean requiresUser() {
        return false;
    }

    @Override
    public boolean configuredFor(KeycloakSession session, RealmModel realm, UserModel user) {
        return true;
    }

    @Override
    public void setRequiredActions(KeycloakSession session, RealmModel realm, UserModel user) {
    }

    @Override
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public String getDisplayType() {
        return "Lantern: cashier check";
    }

    @Override
    public String getReferenceCategory() {
        return null;
    }

    @Override
    public boolean isConfigurable() {
        return false;
    }

    @Override
    public AuthenticationExecutionModel.Requirement[] getRequirementChoices() {
        return REQUIREMENT_CHOICES;
    }

    @Override
    public String getHelpText() {
        return "Resolves 'username' to an enabled cashier in the same outlet as the till.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return new LinkedList<>();
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }
}
```

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/PinCheckAuthenticator.java`:
```java
package dev.lantern.keycloak;

import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;
import java.util.LinkedList;
import java.util.List;
import java.util.Optional;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.directgrant.AbstractDirectGrantAuthenticator;
import org.keycloak.authentication.authenticators.util.AuthenticatorUtils;
import org.keycloak.events.Errors;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserLoginFailureModel;
import org.keycloak.models.UserModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Step 3 of till-cashier-pin: checks the PIN, respects brute-force lockout, and replaces a
 * temporary PIN in the same request (spec §6.2–6.4).
 *
 * Wrong PINs call failure() with the user set, so Keycloak's brute-force protector counts them.
 * Everything else (missing PIN, locked, change required, rule broken) uses challenge(), which returns
 * the same JSON error without counting.
 */
public class PinCheckAuthenticator extends AbstractDirectGrantAuthenticator {

    public static final String PROVIDER_ID = "lantern-pin-check";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        UserModel cashier = context.getUser();
        MultivaluedMap<String, String> form = context.getHttpRequest().getDecodedFormParameters();
        String pin = form.getFirst("pin");
        String newPin = form.getFirst("new_pin");

        if (pin == null || pin.isBlank()) {
            reject(context, TillErrors.PIN_MISSING);
            return;
        }
        if (AuthenticatorUtils.getDisabledByBruteForceEventError(context, cashier) != null) {
            context.getEvent().user(cashier).error(Errors.USER_TEMPORARILY_DISABLED);
            reject(context, TillErrors.PIN_LOCKED);
            return;
        }

        switch (PinCredentials.check(context.getSession(), cashier, pin)) {
            case NO_PIN, MISMATCH -> {
                int failuresSoFar = failuresSoFar(context, cashier);
                context.getEvent().user(cashier).error(Errors.INVALID_USER_CREDENTIALS);
                context.failure(AuthenticationFlowError.INVALID_CREDENTIALS, errorResponse(
                        Response.Status.BAD_REQUEST.getStatusCode(), "invalid_grant",
                        TillErrors.pinInvalid(context.getRealm().getFailureFactor(), failuresSoFar)));
            }
            case OK_TEMPORARY -> {
                if (newPin == null) {
                    reject(context, TillErrors.PIN_CHANGE_REQUIRED);
                    return;
                }
                Optional<String> problem = PinRules.checkNewPin(newPin, pin);
                if (problem.isPresent()) {
                    reject(context, problem.get());
                    return;
                }
                PinCredentials.set(context.getSession(), cashier, newPin, false);
                context.success();
            }
            case OK -> context.success();
        }
    }

    private static int failuresSoFar(AuthenticationFlowContext context, UserModel user) {
        UserLoginFailureModel failures = context.getSession().loginFailures().getUserLoginFailure(context.getRealm(), user.getId());
        return failures == null ? 0 : failures.getNumFailures();
    }

    private void reject(AuthenticationFlowContext context, String code) {
        context.challenge(errorResponse(Response.Status.BAD_REQUEST.getStatusCode(), "invalid_grant", code));
    }

    @Override
    public boolean requiresUser() {
        return true;
    }

    @Override
    public boolean configuredFor(KeycloakSession session, RealmModel realm, UserModel user) {
        return true;
    }

    @Override
    public void setRequiredActions(KeycloakSession session, RealmModel realm, UserModel user) {
    }

    @Override
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public String getDisplayType() {
        return "Lantern: PIN check";
    }

    @Override
    public String getReferenceCategory() {
        return PinCredentials.TYPE;
    }

    @Override
    public boolean isConfigurable() {
        return false;
    }

    @Override
    public AuthenticationExecutionModel.Requirement[] getRequirementChoices() {
        return REQUIREMENT_CHOICES;
    }

    @Override
    public String getHelpText() {
        return "Validates 'pin' against the cashier's lantern-pin credential; 'new_pin' replaces a temporary PIN.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return new LinkedList<>();
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }
}
```

`keycloak/pin-authenticator/src/main/resources/META-INF/services/org.keycloak.authentication.AuthenticatorFactory`:
```
dev.lantern.keycloak.OutletSessionCheckAuthenticator
dev.lantern.keycloak.CashierCheckAuthenticator
dev.lantern.keycloak.PinCheckAuthenticator
```

- [ ] **Step 4: Add the flow, bind it to the till client, and map `till_account`**

```bash
python3 - <<'EOF'
import json, pathlib
p = pathlib.Path("keycloak/realm/lantern-realm.json")
realm = json.loads(p.read_text())

# A PIN pad gets double taps; Keycloak's "2 failures within 1 s" quick lock would trip on one.
realm["quickLoginCheckMilliSeconds"] = 0

def step(provider, priority):
    return {"authenticator": provider, "requirement": "REQUIRED", "priority": priority,
            "authenticatorFlow": False, "userSetupAllowed": False}

realm.setdefault("authenticationFlows", []).append({
    "id": "6a1d3f52-8c1e-4b7a-9f20-3e5b7c9d0a03",
    "alias": "till-cashier-pin",
    "description": "Cashier PIN sign-in at a till with a live outlet login (spec 6.2)",
    "providerId": "basic-flow", "topLevel": True, "builtIn": False,
    "authenticationExecutions": [
        step("lantern-outlet-session-check", 10),
        step("lantern-cashier-check", 20),
        step("lantern-pin-check", 30),
    ],
})

till = next(c for c in realm["clients"] if c["clientId"] == "till")
till["directAccessGrantsEnabled"] = True
till["attributes"]["client.session.idle.timeout"] = "900"
till.setdefault("authenticationFlowBindingOverrides", {})["direct_grant"] = "6a1d3f52-8c1e-4b7a-9f20-3e5b7c9d0a03"
till["protocolMappers"].append({
    "name": "till_account", "protocol": "openid-connect", "protocolMapper": "oidc-usersessionmodel-note-mapper",
    "config": {"user.session.note": "till_account", "claim.name": "till_account", "jsonType.label": "String",
               "access.token.claim": "true", "id.token.claim": "true", "introspection.token.claim": "true"}})

p.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n")
EOF
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~CashierPinFlowTests`
Expected: PASS (14 tests). Specific failures point to specific problems:
- **`Wrong_pin_reports_remaining_attempts` sees `pin_invalid:4` twice:** the failure count isn't visible within the same request. Spec risk §12.2 then needs a ruling. Check whether `Five_wrong_pins…` still locks before changing anything.
- **`Five_wrong_pins…` returns 200:** Keycloak isn't counting the failures at all. That's risk §12.2 materialising, so stop and debug with `docker logs` of the test container (look for `LOGIN_ERROR … invalid_user_credentials`).

Then run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add keycloak tests
git commit -m "feat: cashier pin sign-in flow with lockout and temporary pins"
```

---

### Task 5: One live till login per account (`browser-till` flow)

**Files:**
- Create: `keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/TillAccountLimitAuthenticator.java`
- Modify: `keycloak/pin-authenticator/src/main/resources/META-INF/services/org.keycloak.authentication.AuthenticatorFactory`, `keycloak/realm/lantern-realm.json`
- Test: `tests/Lantern.IntegrationTests/TillAccountLimitTests.cs`

**Interfaces:**
- Consumes: `CashierRoles` (Task 3), `OidcBrowser` and fixture till helpers (Task 1).
- Produces:
  - Authenticator id `lantern-till-account-limit`. It refuses with a 403 page through `forceChallenge`, so refusals are not counted toward lockout.
  - Realm flow `browser-till`: password form, then deny unless `outlet-device`, then the account limit. It has **no cookie step**.
  - The flow is bound as the `till` client's `browser` override.
  - Page texts:
    - "This till account is already signed in on another till. Ask your outlet manager to release it."
    - "This account can't open a till."

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/TillAccountLimitTests.cs`:
```csharp
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.1 E: one till account can be signed in on only one till at a time.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class TillAccountLimitTests(KeycloakFixture kc)
{
    private const string InUse = "already signed in on another till";

    private async Task<LoginOutcome> SecondTillLoginAsync(string account)
    {
        using var browser = new OidcBrowser(kc.Issuer);
        return await browser.LoginAsync(account, KeycloakFixture.DemoPassword);
    }

    [Fact]
    public async Task Second_till_cannot_sign_in_with_the_same_account()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await kc.OutletLoginAsync(account);

        var second = await SecondTillLoginAsync(account);

        Assert.False(second.Succeeded);
        Assert.Contains(InUse, second.Html);
    }

    [Fact]
    public async Task Refused_even_while_the_first_tills_online_session_is_open()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await kc.OutletLoginAsync(account, endOnlineSession: false);

        var second = await SecondTillLoginAsync(account);

        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task Same_browser_cannot_skip_the_password_and_the_limit()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        using var browser = new OidcBrowser(kc.Issuer);
        Assert.True((await browser.LoginAsync(account, KeycloakFixture.DemoPassword)).Succeeded);

        var again = await browser.LoginAsync(account, KeycloakFixture.DemoPassword);

        Assert.False(again.Succeeded);
        Assert.Contains(InUse, again.Html);
    }

    [Fact]
    public async Task Releasing_the_account_lets_another_till_sign_in()
    {
        var (id, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await kc.OutletLoginAsync(account);
        Assert.False((await SecondTillLoginAsync(account)).Succeeded);

        await kc.RevokeTillLoginAsync(id);

        Assert.True((await SecondTillLoginAsync(account)).Succeeded);
    }

    [Fact]
    public async Task Non_outlet_account_cannot_open_a_till()
    {
        var outcome = await SecondTillLoginAsync("eric.procure");

        Assert.False(outcome.Succeeded);
        Assert.Contains("can't open a till", outcome.Html);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~TillAccountLimitTests`
Expected: FAIL (5 tests). Every second login succeeds, and `eric.procure` signs in to the till.

- [ ] **Step 3: Implement the authenticator and register it**

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/TillAccountLimitAuthenticator.java`:
```java
package dev.lantern.keycloak;

import jakarta.ws.rs.core.Response;
import java.util.List;
import org.keycloak.Config;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Refuses an outlet sign-in when the account already has a live offline till login (spec §6.1 E).
 * Keycloak's built-in session limiter only counts online sessions, and the till ends its online one.
 * Uses forceChallenge so refusals don't count toward the account's brute-force lockout.
 */
public class TillAccountLimitAuthenticator implements Authenticator, AuthenticatorFactory {

    public static final String PROVIDER_ID = "lantern-till-account-limit";
    static final String IN_USE_MESSAGE =
            "This till account is already signed in on another till. Ask your outlet manager to release it.";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        UserModel account = context.getUser();
        ClientModel till = context.getAuthenticationSession().getClient();
        boolean inUse = context.getSession().sessions().getOfflineUserSessionsStream(context.getRealm(), account)
                .anyMatch(s -> s.getAuthenticatedClientSessionByClient(till.getId()) != null);
        if (inUse) {
            context.getEvent().user(account).error("till_account_in_use");
            context.forceChallenge(context.form().setError(IN_USE_MESSAGE).createErrorPage(Response.Status.FORBIDDEN));
            return;
        }
        context.success();
    }

    @Override
    public void action(AuthenticationFlowContext context) {
    }

    @Override
    public boolean requiresUser() {
        return true;
    }

    @Override
    public boolean configuredFor(KeycloakSession session, RealmModel realm, UserModel user) {
        return true;
    }

    @Override
    public void setRequiredActions(KeycloakSession session, RealmModel realm, UserModel user) {
    }

    @Override
    public Authenticator create(KeycloakSession session) {
        return this;
    }

    @Override
    public void init(Config.Scope config) {
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Lantern: one till per account";
    }

    @Override
    public String getReferenceCategory() {
        return null;
    }

    @Override
    public boolean isConfigurable() {
        return false;
    }

    @Override
    public AuthenticationExecutionModel.Requirement[] getRequirementChoices() {
        return REQUIREMENT_CHOICES;
    }

    @Override
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public String getHelpText() {
        return "Refuses sign-in when the account already has a live offline login for this client.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return List.of();
    }
}
```

`keycloak/pin-authenticator/src/main/resources/META-INF/services/org.keycloak.authentication.AuthenticatorFactory`: add a fourth line:
```
dev.lantern.keycloak.TillAccountLimitAuthenticator
```

- [ ] **Step 4: Add the browser flow and bind it**

```bash
python3 - <<'EOF'
import json, pathlib
p = pathlib.Path("keycloak/realm/lantern-realm.json")
realm = json.loads(p.read_text())

realm.setdefault("authenticatorConfig", []).extend([
    {"alias": "till-require-outlet-device", "config": {"condUserRole": "outlet-device", "negate": "true"}},
    {"alias": "till-deny-message", "config": {"denyErrorMessage": "This account can't open a till."}},
])

def step(provider, priority, config=None):
    s = {"authenticator": provider, "requirement": "REQUIRED", "priority": priority,
         "authenticatorFlow": False, "userSetupAllowed": False}
    if config: s["authenticatorConfig"] = config
    return s

realm["authenticationFlows"].extend([
    {
        "id": "6a1d3f52-8c1e-4b7a-9f20-3e5b7c9d0a01",
        "alias": "browser-till",
        "description": "Outlet account sign-in for the till: password, outlet-device only, one till per account. No cookie step.",
        "providerId": "basic-flow", "topLevel": True, "builtIn": False,
        "authenticationExecutions": [
            step("auth-username-password-form", 10),
            {"requirement": "CONDITIONAL", "priority": 20, "authenticatorFlow": True,
             "flowAlias": "browser-till outlet-device only", "userSetupAllowed": False},
            step("lantern-till-account-limit", 30),
        ],
    },
    {
        "id": "6a1d3f52-8c1e-4b7a-9f20-3e5b7c9d0a02",
        "alias": "browser-till outlet-device only",
        "description": "Deny anyone without the outlet-device role",
        "providerId": "basic-flow", "topLevel": False, "builtIn": False,
        "authenticationExecutions": [
            step("conditional-user-role", 10, "till-require-outlet-device"),
            step("deny-access-authenticator", 20, "till-deny-message"),
        ],
    },
])

till = next(c for c in realm["clients"] if c["clientId"] == "till")
till["authenticationFlowBindingOverrides"]["browser"] = "6a1d3f52-8c1e-4b7a-9f20-3e5b7c9d0a01"
p.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n")
EOF
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~TillAccountLimitTests`
Expected: PASS (5 tests).
Then run: `dotnet test`
Expected: all pass. Earlier till tests always use fresh temporary accounts, so the limit never blocks them.

- [ ] **Step 6: Commit**

```bash
git add keycloak tests
git commit -m "feat: one live till login per outlet account"
```

---

### Task 6: Sales and receipts in the API

**Files:**
- Create: `src/Lantern.Api/Endpoints/SalesEndpoints.cs`
- Modify: `src/Lantern.Api/Auth/OutletRequirement.cs`, `src/Lantern.Api/Auth/Policies.cs`, `src/Lantern.Api/Data/DemoStore.cs`, `src/Lantern.Api/Program.cs`, `tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`
- Test: `tests/Lantern.IntegrationTests/SalesTests.cs`

**Interfaces:**
- Consumes: cashier tokens with `till_account` (Task 4), `OpenTill` and `CashierPinAsync` (Task 4), and Plan 1's `ApiFactory` and `GetSub`/`GetDisplayName`.
- Produces:
  - `OutletRequirement(IReadOnlyList<string> AllowedRoles, bool HqAdminBypass)`.
  - Policies `OutletSell` (cashier, no bypass) and `OutletReceipts` (cashier or outlet-manager, hq-admin bypass).
  - `POST /outlets/{outletId}/sales` with body `{"items":[{"sku":"SKU-100","quantity":2}]}` → 201 `Receipt`.
  - `GET /outlets/{outletId}/sales/{saleId}/receipt`.
  - `Receipt(Guid SaleId, string OutletId, string TillAccount, string CashierId, string CashierCode, string CashierName, string ServedBy, IReadOnlyList<ReceiptLine> Lines, decimal Total, DateTimeOffset At)`. `ServedBy` = `"{name} ({code})"`.
  - Fixture `CashierTokenAsync(OpenTill till, string username, string pin)` → `string`.

- [ ] **Step 1: Write the fixture helper and the failing tests**

`tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`: add after `ErrorCode`:
```csharp
    public async Task<string> CashierTokenAsync(OpenTill till, string username, string pin)
    {
        var (status, body) = await CashierPinAsync(till.AccessToken, username, pin);
        if (status != HttpStatusCode.OK) throw new InvalidOperationException($"PIN sign-in for {username} failed: {body}");
        return (string)body["access_token"]!;
    }
```

`tests/Lantern.IntegrationTests/SalesTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class SalesTests(KeycloakFixture kc)
{
    private static readonly object TwoCoffeesOneMilk = new
    {
        items = new object[] { new { sku = "SKU-100", quantity = 2 }, new { sku = "SKU-200", quantity = 1 } }
    };

    private async Task<HttpResponseMessage> RingAsync(string token, string outlet, object? sale = null) =>
        await kc.Api.ClientWithToken(token).PostAsJsonAsync($"/outlets/{outlet}/sales", sale ?? TwoCoffeesOneMilk);

    [Fact]
    public async Task Cashier_rings_a_sale_and_receipt_names_cashier_and_till()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var token = await kc.CashierTokenAsync(till, "c-1001", "1111");

        var response = await RingAsync(token, "BGS");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Siti Aminah (c-1001)", receipt.GetProperty("servedBy").GetString());
        Assert.Equal(till.Account, receipt.GetProperty("tillAccount").GetString());
        Assert.Equal("BGS", receipt.GetProperty("outletId").GetString());
        Assert.Equal(99.50m, receipt.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Same_cashier_on_two_tills_gets_each_till_on_its_receipt()
    {
        var tillA = await kc.OpenTillAsync("/Outlets/Bangsar");
        var tillB = await kc.OpenTillAsync("/Outlets/Bangsar");

        var onA = await (await RingAsync(await kc.CashierTokenAsync(tillA, "c-1002", "2222"), "BGS")).Content.ReadFromJsonAsync<JsonElement>();
        var onB = await (await RingAsync(await kc.CashierTokenAsync(tillB, "c-1002", "2222"), "BGS")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(tillA.Account, onA.GetProperty("tillAccount").GetString());
        Assert.Equal(tillB.Account, onB.GetProperty("tillAccount").GetString());
    }

    [Fact]
    public async Task Cashier_cannot_sell_for_another_outlet()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var response = await RingAsync(await kc.CashierTokenAsync(till, "c-1001", "1111"), "KLC");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("OutletSell", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("policy").GetString());
    }

    [Fact]
    public async Task Outlet_account_itself_cannot_ring_sales()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        Assert.Equal(HttpStatusCode.Forbidden, (await RingAsync(till.AccessToken, "BGS")).StatusCode);
    }

    [Fact]
    public async Task Hq_admin_cannot_ring_sales()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await RingAsync(await kc.GetUserTokenAsync("aisha.admin"), "BGS")).StatusCode);
    }

    [Theory]
    [InlineData("mgr.bangsar", HttpStatusCode.OK)]
    [InlineData("aisha.admin", HttpStatusCode.OK)]
    [InlineData("mgr.klcc", HttpStatusCode.Forbidden)]
    public async Task Receipts_are_readable_by_that_outlets_manager(string reader, HttpStatusCode expected)
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var sale = await (await RingAsync(await kc.CashierTokenAsync(till, "c-1001", "1111"), "BGS")).Content.ReadFromJsonAsync<JsonElement>();

        var response = await (await kc.Api.ClientAsAsync(reader)).GetAsync($"/outlets/BGS/sales/{sale.GetProperty("saleId").GetString()}/receipt");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_sale_is_404_with_reason()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).GetAsync($"/outlets/BGS/sales/{Guid.NewGuid()}/receipt");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Sale not found.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    public static TheoryData<object> InvalidSales => new()
    {
        new { items = Array.Empty<object>() },
        new { items = new object[] { new { sku = "SKU-999", quantity = 1 } } },
        new { items = new object[] { new { sku = "SKU-100", quantity = 0 } } }
    };

    [Theory]
    [MemberData(nameof(InvalidSales))]
    public async Task Invalid_sale_is_400(object sale)
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var response = await RingAsync(await kc.CashierTokenAsync(till, "c-1001", "1111"), "BGS", sale);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~SalesTests`
Expected: FAIL. The sales routes don't exist, so the tests get 404 where they expect 201/403/400/200, and the "Sale not found." title assertion fails too. `Receipts_are_readable…` fails at reading the sale body.

- [ ] **Step 3: Make the hq-admin bypass opt-in, and add the policies**

`src/Lantern.Api/Auth/OutletRequirement.cs` (replace the whole file):
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Auth;

/// <summary>
/// User must hold one of <see cref="AllowedRoles"/> and belong to the outlet named in the route.
/// hq-admin passes only when <see cref="HqAdminBypass"/> is set (reading, yes; ringing sales, no).
/// </summary>
public sealed record OutletRequirement(IReadOnlyList<string> AllowedRoles, bool HqAdminBypass) : IAuthorizationRequirement;

public sealed class OutletRequirementHandler : AuthorizationHandler<OutletRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OutletRequirement requirement)
    {
        if (context.Resource is not HttpContext http) return Task.CompletedTask;
        if (http.GetRouteValue("outletId") is not string routeOutlet || routeOutlet.Length == 0) return Task.CompletedTask;

        if (requirement.HqAdminBypass && context.User.IsInRole(Roles.HqAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userOutlet = context.User.FindFirstValue("outlet_id");
        if (requirement.AllowedRoles.Any(context.User.IsInRole) &&
            string.Equals(userOutlet, routeOutlet, StringComparison.OrdinalIgnoreCase))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

`src/Lantern.Api/Auth/Policies.cs`:
- add constants after `OutletManage`:
```csharp
    public const string OutletSell = "OutletSell";
    public const string OutletReceipts = "OutletReceipts";
```
- replace the `OutletManage` policy line with:
```csharp
            .AddPolicy(OutletManage, p => p.RequireAuthenticatedUser().AddRequirements(new OutletRequirement([Roles.OutletManager], HqAdminBypass: true)))
            .AddPolicy(OutletSell, p => p.RequireAuthenticatedUser().AddRequirements(new OutletRequirement([Roles.Cashier], HqAdminBypass: false)))
            .AddPolicy(OutletReceipts, p => p.RequireAuthenticatedUser().AddRequirements(new OutletRequirement([Roles.Cashier, Roles.OutletManager], HqAdminBypass: true)))
```

- [ ] **Step 4: Add the catalog, sales and receipts**

`src/Lantern.Api/Data/DemoStore.cs`: add records next to the others:
```csharp
public sealed record CatalogItem(string Name, decimal Price);
public sealed record ReceiptLine(string Sku, string Name, int Quantity, decimal UnitPrice, decimal Amount);
public sealed record Receipt(Guid SaleId, string OutletId, string TillAccount, string CashierId, string CashierCode,
    string CashierName, string ServedBy, IReadOnlyList<ReceiptLine> Lines, decimal Total, DateTimeOffset At);
```
and these members inside `DemoStore`:
```csharp
    public IReadOnlyDictionary<string, CatalogItem> Catalog { get; } =
        new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["SKU-100"] = new("House Blend 1kg", 45.00m),
            ["SKU-200"] = new("Oat Milk 1L", 9.50m)
        };

    private readonly ConcurrentDictionary<Guid, Receipt> _receipts = new();

    public Receipt RecordSale(string outletId, string tillAccount, string cashierId, string cashierCode, string cashierName,
        IReadOnlyList<(string Sku, int Quantity)> items)
    {
        var lines = items.Select(i =>
        {
            var item = Catalog[i.Sku];
            return new ReceiptLine(i.Sku.ToUpperInvariant(), item.Name, i.Quantity, item.Price, item.Price * i.Quantity);
        }).ToList();
        var receipt = new Receipt(Guid.NewGuid(), outletId, tillAccount, cashierId, cashierCode, cashierName,
            $"{cashierName} ({cashierCode})", lines, lines.Sum(l => l.Amount), DateTimeOffset.UtcNow);
        _receipts[receipt.SaleId] = receipt;
        return receipt;
    }

    public Receipt? FindReceipt(string outletId, Guid saleId) =>
        _receipts.TryGetValue(saleId, out var receipt) &&
        string.Equals(receipt.OutletId, outletId, StringComparison.OrdinalIgnoreCase)
            ? receipt
            : null;
```

`src/Lantern.Api/Endpoints/SalesEndpoints.cs`:
```csharp
using System.Security.Claims;
using Lantern.Api.Auth;
using Lantern.Api.Data;

namespace Lantern.Api.Endpoints;

public sealed record SaleLine(string? Sku, int Quantity);
public sealed record NewSale(SaleLine[]? Items);

public static class SalesEndpoints
{
    public static void MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var sales = app.MapGroup("/outlets/{outletId}/sales");

        sales.MapPost("/", (string outletId, NewSale body, ClaimsPrincipal user, DemoStore store) =>
            {
                var errors = Validate(body, store);
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                var till = user.FindFirstValue("till_account");
                if (string.IsNullOrEmpty(till))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Sales must come from a till.");

                var receipt = store.RecordSale(outletId.ToUpperInvariant(), till, user.GetSub(),
                    user.Identity?.Name ?? "", user.GetDisplayName(),
                    body.Items!.Select(i => (i.Sku!, i.Quantity)).ToList());
                return Results.Created($"/outlets/{receipt.OutletId}/sales/{receipt.SaleId}/receipt", receipt);
            })
            .RequireAuthorization(Policies.OutletSell);

        sales.MapGet("/{saleId:guid}/receipt", (string outletId, Guid saleId, DemoStore store) =>
                store.FindReceipt(outletId, saleId) is { } receipt
                    ? Results.Ok(receipt)
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Sale not found."))
            .RequireAuthorization(Policies.OutletReceipts);
    }

    private static Dictionary<string, string[]> Validate(NewSale body, DemoStore store)
    {
        var errors = new Dictionary<string, string[]>();
        if (body.Items is null || body.Items.Length == 0)
            errors["items"] = ["A sale needs at least one item."];
        else if (body.Items.Any(i => i.Sku is null || !store.Catalog.ContainsKey(i.Sku)))
            errors["items"] = ["Unknown SKU."];
        else if (body.Items.Any(i => i.Quantity is < 1 or > 999))
            errors["items"] = ["Quantity must be between 1 and 999."];
        return errors;
    }
}
```

`src/Lantern.Api/Program.cs`: add after `app.MapStaffEndpoints();`:
```csharp
app.MapSalesEndpoints();
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter "FullyQualifiedName~SalesTests|FullyQualifiedName~OutletScopeTests"`
Expected: PASS (all `SalesTests` plus Plan 1's 12 outlet tests, which prove `OutletManage` behaves as before).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: till sales and receipts naming cashier and till"
```

---

### Task 7: Till accounts in the API (list, add, release) and deactivating a till

**Files:**
- Create: `src/Lantern.Api/Data/Outlets.cs`, `src/Lantern.Api/Endpoints/TillEndpoints.cs`
- Modify: `src/Lantern.Api/Keycloak/KeycloakModels.cs`, `src/Lantern.Api/Keycloak/KeycloakAdminClient.cs`, `src/Lantern.Api/Program.cs`, `keycloak/realm/lantern-realm.json`
- Test: `tests/Lantern.IntegrationTests/TillAccountTests.cs`

**Interfaces:**
- Consumes:
  - `Policies.OutletManage`, `Policies.StaffAdmin`, `Roles.OutletDevice`
  - Plan 1 `KeycloakAdminClient` internals: `GetJsonAsync`, `SendAsync`, `SendOkAsync`, `ReadValidationErrorAsync`, `cache`
  - fixture `OpenTillAsync`, `OutletLoginAsync`, `CashierPinAsync`
- Produces:
  - `Outlet(string Id, string Name, string GroupPath, string Slug, char CashierDigit)`, with `Outlets.All` and `Outlets.Find(string id)`.
  - `KeycloakAdminClient` additions:
    - `GetTillClientUuidAsync`, `HasRealmRoleAsync`, `GetOutletMembersWithRoleAsync`
    - `ListTillsAsync(Outlet)` → `IReadOnlyList<TillAccount>`
    - `CreateTillAccountAsync(Outlet, string password)` → `UserCreation`
    - `ReleaseTillAsync(string userId)` → `bool`
    - private `CreateUserAsync(object body, string username)` → `UserCreation`
    - private `GrantRealmRoleAsync(userId, role)`
  - Models:
    - `TillAccount(string Id, string Username, bool Enabled, TillSession? Session)`
    - `TillSession(DateTimeOffset StartedAt, DateTimeOffset LastUsedAt, string? IpAddress)`
    - `UserCreation(string? Id, string Username, bool Conflict, Dictionary<string, string[]>? Errors)`
  - Routes:
    - `GET /outlets/{outletId}/tills` (`OutletManage`)
    - `POST /outlets/{outletId}/tills` with body `{"password":"…"}` (`StaffAdmin`)
    - `DELETE /outlets/{outletId}/tills/{userId}/session` (`OutletManage`)
  - Realm: the service account also has `query-clients`.

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/TillAccountTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class TillAccountTests(KeycloakFixture kc)
{
    private async Task<JsonElement> TillsAsync(string reader, string outlet) =>
        await (await kc.Api.ClientAsAsync(reader)).GetFromJsonAsync<JsonElement>($"/outlets/{outlet}/tills");

    private static JsonElement Find(JsonElement tills, string username) =>
        tills.EnumerateArray().Single(t => t.GetProperty("username").GetString() == username);

    [Fact]
    public async Task Manager_sees_own_outlets_tills_and_which_are_signed_in()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");

        var tills = await TillsAsync("mgr.bangsar", "BGS");

        var signedIn = Find(tills, till.Account).GetProperty("session");
        Assert.True(signedIn.GetProperty("startedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.False(string.IsNullOrEmpty(signedIn.GetProperty("ipAddress").GetString()));
        Assert.Equal(JsonValueKind.Null, Find(tills, "outlet-bangsar-2").GetProperty("session").ValueKind);
    }

    [Fact]
    public async Task Other_outlets_manager_cannot_see_the_tills()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.klcc")).GetAsync("/outlets/BGS/tills");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Release_ends_the_till_login_and_frees_the_account()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");

        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).DeleteAsync($"/outlets/BGS/tills/{till.AccountId}/session");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await kc.RefreshTillTokenAsync(till.OfflineRefreshToken)).Status);
        await kc.OutletLoginAsync(till.Account); // would throw if the account were still taken
    }

    [Fact]
    public async Task Releasing_a_till_that_is_not_signed_in_is_404()
    {
        var (id, _) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).DeleteAsync($"/outlets/BGS/tills/{id}/session");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Till is not signed in.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Releasing_another_outlets_till_through_your_outlet_is_404()
    {
        var klccTill = await kc.GetUserIdAsync("outlet-klcc-1");
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).DeleteAsync($"/outlets/BGS/tills/{klccTill}/session");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Till not found in this outlet.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Hq_admin_adds_tills_with_increasing_numbers_and_they_work()
    {
        var admin = await kc.Api.ClientAsAsync("aisha.admin");

        var first = await (await admin.PostAsJsonAsync("/outlets/PJY/tills", new { password = "Lantern!2026" })).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await admin.PostAsJsonAsync("/outlets/PJY/tills", new { password = "Lantern!2026" })).Content.ReadFromJsonAsync<JsonElement>();

        var firstName = first.GetProperty("username").GetString()!;
        var secondName = second.GetProperty("username").GetString()!;
        Assert.StartsWith("outlet-pj-", firstName);
        Assert.Equal(int.Parse(firstName["outlet-pj-".Length..]) + 1, int.Parse(secondName["outlet-pj-".Length..]));

        var tokens = await kc.OutletLoginAsync(secondName);
        var claims = Jwt.Payload((string)(await kc.RefreshTillTokenAsync(tokens.RefreshToken)).Body["access_token"]!);
        Assert.Equal("PJY", (string?)claims["outlet_id"]);
        Assert.Contains("outlet-device", claims["roles"]!.AsArray().Select(r => (string?)r));
    }

    [Fact]
    public async Task Short_till_password_is_400()
    {
        var response = await (await kc.Api.ClientAsAsync("aisha.admin")).PostAsJsonAsync("/outlets/PJY/tills", new { password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_add_tills()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.pj")).PostAsJsonAsync("/outlets/PJY/tills", new { password = "Lantern!2026" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deactivating_a_till_account_stops_its_till()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");

        var response = await (await kc.Api.ClientAsAsync("aisha.admin")).PostAsync($"/staff/{till.AccountId}/deactivate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await kc.RefreshTillTokenAsync(till.OfflineRefreshToken)).Status);
        var (_, body) = await kc.CashierPinAsync(till.AccessToken, "c-1001", "1111");
        Assert.Equal("outlet_session_invalid", KeycloakFixture.ErrorCode(body));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~TillAccountTests`
Expected: FAIL. The till routes don't exist (404, and JSON reads fail). `Deactivating_a_till_account_stops_its_till` passes already: Plan 1's deactivation revokes `consents/till`. It stays as the spec §9 F guard.

- [ ] **Step 3: Grant `query-clients` to the service account**

The API needs the `till` client's internal id for `users/{id}/offline-sessions/{clientUuid}`.

```bash
python3 - <<'EOF'
import json, pathlib
p = pathlib.Path("keycloak/realm/lantern-realm.json")
realm = json.loads(p.read_text())
sa = next(u for u in realm["users"] if u["username"] == "service-account-api-admin-svc")
roles = sa["clientRoles"]["realm-management"]
if "query-clients" not in roles: roles.append("query-clients")
p.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n")
EOF
```

- [ ] **Step 4: Implement**

`src/Lantern.Api/Data/Outlets.cs`:
```csharp
namespace Lantern.Api.Data;

/// <summary>Outlet id ↔ Keycloak group, and the naming used for its till accounts and cashier codes.</summary>
public sealed record Outlet(string Id, string Name, string GroupPath, string Slug, char CashierDigit);

public static class Outlets
{
    public static readonly IReadOnlyList<Outlet> All =
    [
        new("BGS", "Bangsar", "/Outlets/Bangsar", "bangsar", '1'),
        new("KLC", "KLCC", "/Outlets/KLCC", "klcc", '2'),
        new("PJY", "PJ", "/Outlets/PJ", "pj", '3')
    ];

    public static Outlet? Find(string id) =>
        All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
}
```

`src/Lantern.Api/Keycloak/KeycloakModels.cs`: append:
```csharp
public sealed record KcClient(string Id, string ClientId);
public sealed record KcUserSession(string Id, string? IpAddress, long Start, long LastAccess);

public sealed record TillSession(DateTimeOffset StartedAt, DateTimeOffset LastUsedAt, string? IpAddress);
public sealed record TillAccount(string Id, string Username, bool Enabled, TillSession? Session);

/// <summary>Outcome of creating a Keycloak user: an id, a username conflict, or Keycloak's validation errors.</summary>
public sealed record UserCreation(string? Id, string Username, bool Conflict, Dictionary<string, string[]>? Errors);
```

`src/Lantern.Api/Keycloak/KeycloakAdminClient.cs`:
- add `using Lantern.Api.Data;` to the usings
- add a constant next to `TokenCacheKey`:
```csharp
    private const string TillClientCacheKey = "kc-till-client-uuid";
```
- add these members before `private async Task SetEnabledAsync`:
```csharp
    public async Task<string> GetTillClientUuidAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(TillClientCacheKey, out string? cached) && cached is not null) return cached;
        var uuid = (await GetJsonAsync<List<KcClient>>("clients?clientId=till", ct)).Single().Id;
        cache.Set(TillClientCacheKey, uuid);
        return uuid;
    }

    public async Task<bool> HasRealmRoleAsync(string userId, string role, CancellationToken ct) =>
        (await GetJsonAsync<List<KcRole>>($"users/{userId}/role-mappings/realm/composite", ct)).Any(r => r.Name == role);

    public async Task<IReadOnlyList<KcUser>> GetOutletMembersWithRoleAsync(Outlet outlet, string role, CancellationToken ct)
    {
        var group = await GetJsonAsync<KcGroup>($"group-by-path/{outlet.GroupPath.TrimStart('/')}", ct);
        var members = await GetJsonAsync<List<KcUser>>($"groups/{group.Id}/members?briefRepresentation=true&max=500", ct);
        var result = new List<KcUser>();
        foreach (var member in members)
            if (await HasRealmRoleAsync(member.Id, role, ct)) result.Add(member);
        return result;
    }

    public async Task<IReadOnlyList<TillAccount>> ListTillsAsync(Outlet outlet, CancellationToken ct)
    {
        var tillClient = await GetTillClientUuidAsync(ct);
        var result = new List<TillAccount>();
        foreach (var user in (await GetOutletMembersWithRoleAsync(outlet, Roles.OutletDevice, ct)).OrderBy(u => u.Username))
        {
            var sessions = await GetJsonAsync<List<KcUserSession>>($"users/{user.Id}/offline-sessions/{tillClient}", ct);
            var latest = sessions.MaxBy(s => s.LastAccess);
            result.Add(new TillAccount(user.Id, user.Username, user.Enabled, latest is null
                ? null
                : new TillSession(DateTimeOffset.FromUnixTimeMilliseconds(latest.Start),
                    DateTimeOffset.FromUnixTimeMilliseconds(latest.LastAccess), latest.IpAddress)));
        }
        return result;
    }

    public async Task<UserCreation> CreateTillAccountAsync(Outlet outlet, string password, CancellationToken ct)
    {
        var prefix = $"outlet-{outlet.Slug}-";
        var next = NextNumber((await GetOutletMembersWithRoleAsync(outlet, Roles.OutletDevice, ct)).Select(u => u.Username), prefix, digits: null);
        var username = $"{prefix}{next}";
        var creation = await CreateUserAsync(new
        {
            username,
            enabled = true,
            firstName = outlet.Name,
            lastName = $"Till {next}",
            email = $"{username}@lantern.test",
            emailVerified = true,
            groups = new[] { outlet.GroupPath },
            credentials = new[] { new { type = "password", value = password, temporary = false } }
        }, username, ct);
        if (creation.Id is not null) await GrantRealmRoleAsync(creation.Id, Roles.OutletDevice, ct);
        return creation;
    }

    /// <returns>False when the account had no till login to release.</returns>
    public async Task<bool> ReleaseTillAsync(string userId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"users/{userId}/consents/till", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Highest existing number after <paramref name="prefix"/> plus one; gaps are never reused.</summary>
    private static int NextNumber(IEnumerable<string> usernames, string prefix, int? digits) =>
        usernames
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && (digits is null || n.Length == prefix.Length + digits))
            .Select(n => int.TryParse(n[prefix.Length..], out var k) ? k : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

    private async Task<UserCreation> CreateUserAsync(object body, string username, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "users", body, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return new UserCreation(null, username, true, null);
        if (response.StatusCode == HttpStatusCode.BadRequest)
            return new UserCreation(null, username, false, await ReadValidationErrorAsync(response, ct));
        response.EnsureSuccessStatusCode();
        return new UserCreation(response.Headers.Location!.Segments.Last(), username, false, null);
    }

    /// <summary>Uses the "available" list, which manage-users can read, to get the role's representation.</summary>
    private async Task GrantRealmRoleAsync(string userId, string roleName, CancellationToken ct)
    {
        var available = await GetJsonAsync<List<JsonObject>>($"users/{userId}/role-mappings/realm/available", ct);
        var role = available.Single(r => (string?)r["name"] == roleName);
        await SendOkAsync(HttpMethod.Post, $"users/{userId}/role-mappings/realm", new[] { role }, ct);
    }
```

`src/Lantern.Api/Endpoints/TillEndpoints.cs`:
```csharp
using Lantern.Api.Auth;
using Lantern.Api.Data;
using Lantern.Api.Keycloak;

namespace Lantern.Api.Endpoints;

public sealed record NewTill(string? Password);

public static class TillEndpoints
{
    public static void MapTillEndpoints(this IEndpointRouteBuilder app)
    {
        var tills = app.MapGroup("/outlets/{outletId}/tills");

        tills.MapGet("/", async (string outletId, KeycloakAdminClient kc, CancellationToken ct) =>
                Outlets.Find(outletId) is { } outlet ? Results.Ok(await kc.ListTillsAsync(outlet, ct)) : OutletNotFound())
            .RequireAuthorization(Policies.OutletManage);

        tills.MapPost("/", async (string outletId, NewTill body, KeycloakAdminClient kc, CancellationToken ct) =>
            {
                if (Outlets.Find(outletId) is not { } outlet) return OutletNotFound();
                if (body.Password is null || body.Password.Length < 8)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Use at least 8 characters."] });

                var created = await kc.CreateTillAccountAsync(outlet, body.Password, ct);
                if (created.Conflict)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "That till name was just taken; try again.");
                if (created.Errors is not null) return Results.ValidationProblem(created.Errors);
                return Results.Created($"/outlets/{outlet.Id}/tills/{created.Id}", new { id = created.Id, username = created.Username });
            })
            .RequireAuthorization(Policies.StaffAdmin);

        tills.MapDelete("/{userId}/session", async (string outletId, string userId, KeycloakAdminClient kc, CancellationToken ct) =>
            {
                if (Outlets.Find(outletId) is not { } outlet) return OutletNotFound();
                var outletTills = await kc.GetOutletMembersWithRoleAsync(outlet, Roles.OutletDevice, ct);
                if (outletTills.All(t => t.Id != userId))
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Till not found in this outlet.");

                return await kc.ReleaseTillAsync(userId, ct)
                    ? Results.NoContent()
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Till is not signed in.");
            })
            .RequireAuthorization(Policies.OutletManage);
    }

    private static IResult OutletNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Outlet not found.");
}
```

`src/Lantern.Api/Program.cs`: add after `app.MapSalesEndpoints();`:
```csharp
app.MapTillEndpoints();
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~TillAccountTests`
Expected: PASS (9 tests). Two likely failures:
- **403 from Keycloak on `role-mappings/realm/available` or on the role POST:** the service account needs one more `realm-management` role. Try `view-realm`, add it with the Step 3 script pattern, and record a ruling.
- **403 on `clients?clientId=till`:** the Step 3 role didn't apply. The realm is only re-imported in fresh containers; tests always start fresh, so check the script ran.

Then run: `dotnet test`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add keycloak/realm src tests
git commit -m "feat: till account listing, creation and release"
```

---

### Task 8: Cashier management in the API (create with a temporary PIN, reset PIN)

**Files:**
- Create: `src/Lantern.Api/Keycloak/PinGenerator.cs`, `src/Lantern.Api/Endpoints/CashierEndpoints.cs`
- Modify: `src/Lantern.Api/Keycloak/KeycloakAdminClient.cs`, `src/Lantern.Api/Program.cs`
- Test: `tests/Lantern.IntegrationTests/CashierManagementTests.cs`

**Interfaces:**
- Consumes:
  - `Outlets`, `UserCreation`, `NextNumber`, `CreateUserAsync`, `GrantRealmRoleAsync`, `GetOutletMembersWithRoleAsync`, `HasRealmRoleAsync` (Task 7)
  - the PIN resource (Task 3)
  - the PIN flow (Task 4)
- Produces:
  - `KeycloakAdminClient`:
    - `CreateCashierAsync(Outlet, string firstName, string lastName, string temporaryPin)` → `UserCreation`, where the code is `c-{digit}{nnn}`
    - `SetPinAsync(string userId, string pin, bool temporary)`
    - private `SendToUrlAsync`
  - `PinGenerator.NewTemporaryPin()` → 6 digits.
  - Routes, both `StaffAdmin`:
    - `POST /cashiers` with body `{"outletId","firstName","lastName"}` → 201 `{id, code, temporaryPin}`
    - `POST /cashiers/{id}/reset-pin` → 200 `{temporaryPin}`

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/CashierManagementTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class CashierManagementTests(KeycloakFixture kc)
{
    private Task<HttpClient> Admin() => kc.Api.ClientAsAsync("aisha.admin");

    private async Task<JsonElement> CreateCashierAsync(string outletId, string first = "Amir", string last = "Hakim")
    {
        var response = await (await Admin()).PostAsJsonAsync("/cashiers", new { outletId, firstName = first, lastName = last });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task New_cashier_signs_in_with_the_temporary_pin_then_chooses_their_own()
    {
        var created = await CreateCashierAsync("PJY");
        var code = created.GetProperty("code").GetString()!;
        var temporaryPin = created.GetProperty("temporaryPin").GetString()!;
        var till = await kc.OpenTillAsync("/Outlets/PJ");

        Assert.StartsWith("c-3", code);
        Assert.Equal("pin_change_required", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, code, temporaryPin)).Body));
        var (status, body) = await kc.CashierPinAsync(till.AccessToken, code, temporaryPin, "2580");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(code, (string?)Jwt.Payload((string)body["access_token"]!)["preferred_username"]);
    }

    [Fact]
    public async Task Codes_are_unique_within_an_outlet()
    {
        var first = (await CreateCashierAsync("KLC")).GetProperty("code").GetString()!;
        var second = (await CreateCashierAsync("KLC")).GetProperty("code").GetString()!;

        Assert.StartsWith("c-2", first);
        Assert.Equal(int.Parse(first[3..]) + 1, int.Parse(second[3..]));
    }

    [Fact]
    public async Task Reset_pin_replaces_the_old_pin_and_lifts_a_lockout()
    {
        var created = await CreateCashierAsync("BGS");
        var id = created.GetProperty("id").GetString()!;
        var code = created.GetProperty("code").GetString()!;
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        await kc.CashierPinAsync(till.AccessToken, code, created.GetProperty("temporaryPin").GetString()!, "2580");
        for (var i = 0; i < 5; i++) await kc.CashierPinAsync(till.AccessToken, code, "0000");
        Assert.Equal("pin_locked", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, code, "2580")).Body));

        var reset = await (await Admin()).PostAsync($"/cashiers/{id}/reset-pin", null);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var newTemporary = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPin").GetString()!;
        Assert.Equal("pin_change_required", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, code, newTemporary)).Body));
        Assert.StartsWith("pin_invalid", KeycloakFixture.ErrorCode((await kc.CashierPinAsync(till.AccessToken, code, "2580")).Body));
    }

    [Fact]
    public async Task Manager_cannot_create_cashiers()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).PostAsJsonAsync("/cashiers",
            new { outletId = "BGS", firstName = "X", lastName = "Y" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("XXX", "Amir", "Hakim")]
    [InlineData("BGS", "", "Hakim")]
    [InlineData("BGS", "Amir (HQ)", "Hakim")]
    public async Task Invalid_cashier_is_400(string outletId, string first, string last)
    {
        var response = await (await Admin()).PostAsJsonAsync("/cashiers", new { outletId, firstName = first, lastName = last });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reset_pin_for_a_non_cashier_is_400()
    {
        var response = await (await Admin()).PostAsync($"/cashiers/{await kc.GetUserIdAsync("eric.procure")}/reset-pin", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reset_pin_for_an_unknown_user_is_404_with_reason()
    {
        var response = await (await Admin()).PostAsync($"/cashiers/{Guid.NewGuid()}/reset-pin", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Cashier not found.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~CashierManagementTests`
Expected: FAIL. `/cashiers` doesn't exist, so tests get 404 where they expect 201/403/400/200, and the "Cashier not found." title assertion fails.

- [ ] **Step 3: Implement**

`src/Lantern.Api/Keycloak/PinGenerator.cs`:
```csharp
using System.Security.Cryptography;

namespace Lantern.Api.Keycloak;

public static class PinGenerator
{
    /// <summary>A random 6-digit temporary PIN; the cashier replaces it at first sign-in (spec §6.4).</summary>
    public static string NewTemporaryPin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
```

`src/Lantern.Api/Keycloak/KeycloakAdminClient.cs`:
- add after `ReleaseTillAsync`:
```csharp
    public async Task<UserCreation> CreateCashierAsync(Outlet outlet, string firstName, string lastName, string temporaryPin, CancellationToken ct)
    {
        var prefix = $"c-{outlet.CashierDigit}";
        var next = NextNumber((await GetOutletMembersWithRoleAsync(outlet, Roles.Cashier, ct)).Select(u => u.Username), prefix, digits: 3);
        var code = $"{prefix}{next:000}";
        var creation = await CreateUserAsync(new
        {
            username = code,
            enabled = true,
            firstName,
            lastName,
            email = $"{code}@lantern.test",
            emailVerified = true,
            groups = new[] { outlet.GroupPath }
        }, code, ct);
        if (creation.Id is null) return creation;

        await GrantRealmRoleAsync(creation.Id, Roles.Cashier, ct);
        await SetPinAsync(creation.Id, temporaryPin, temporary: true, ct);
        return creation;
    }

    /// <summary>Calls the plugin's PIN resource (spec §6.6); it also clears the cashier's lockout.</summary>
    public async Task SetPinAsync(string userId, string pin, bool temporary, CancellationToken ct)
    {
        using var response = await SendToUrlAsync(HttpMethod.Put, $"{Kc.RealmUrl}/lantern-pin/users/{userId}", new { pin, temporary }, ct);
        response.EnsureSuccessStatusCode();
    }
```
- replace the existing `SendAsync` method with this pair:
```csharp
    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct) =>
        SendToUrlAsync(method, $"{Kc.AdminUrl}/{path}", body, ct);

    private async Task<HttpResponseMessage> SendToUrlAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetServiceTokenAsync(ct));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, ct);
    }
```

`src/Lantern.Api/Endpoints/CashierEndpoints.cs`:
```csharp
using Lantern.Api.Auth;
using Lantern.Api.Data;
using Lantern.Api.Keycloak;

namespace Lantern.Api.Endpoints;

public sealed record NewCashier(string? OutletId, string? FirstName, string? LastName);

public static class CashierEndpoints
{
    public static void MapCashierEndpoints(this IEndpointRouteBuilder app)
    {
        var cashiers = app.MapGroup("/cashiers").RequireAuthorization(Policies.StaffAdmin);

        cashiers.MapPost("/", async (NewCashier body, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var outlet = body.OutletId is null ? null : Outlets.Find(body.OutletId);
            if (outlet is null) errors["outletId"] = ["Unknown outlet."];
            if (string.IsNullOrWhiteSpace(body.FirstName)) errors["firstName"] = ["First name is required."];
            if (string.IsNullOrWhiteSpace(body.LastName)) errors["lastName"] = ["Last name is required."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var temporaryPin = PinGenerator.NewTemporaryPin();
            var created = await kc.CreateCashierAsync(outlet!, body.FirstName!.Trim(), body.LastName!.Trim(), temporaryPin, ct);
            if (created.Conflict)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "That cashier code was just taken; try again.");
            if (created.Errors is not null) return Results.ValidationProblem(created.Errors);
            return Results.Created($"/cashiers/{created.Id}", new { id = created.Id, code = created.Username, temporaryPin });
        });

        cashiers.MapPost("/{id}/reset-pin", async (string id, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            if (await kc.GetUserAsync(id, ct) is null)
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Cashier not found.");
            if (!await kc.HasRealmRoleAsync(id, Roles.Cashier, ct))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Not a cashier.");

            var temporaryPin = PinGenerator.NewTemporaryPin();
            await kc.SetPinAsync(id, temporaryPin, temporary: true, ct);
            return Results.Ok(new { temporaryPin });
        });
    }
}
```

`src/Lantern.Api/Program.cs`: add after `app.MapTillEndpoints();`:
```csharp
app.MapCashierEndpoints();
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~CashierManagementTests`
Expected: PASS (9 tests).
Then run: `dotnet test`
Expected: all pass. Plan 1's 79 tests plus this plan's 63 make 142, with theory cases counted individually.
Then run: `docker compose down -v && docker compose up -d --build --wait && ./scripts/smoke.sh`
Expected: `OK: stack healthy`. The realm re-imports with the new flows, and Keycloak starts with the four authenticators and the PIN resource.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: cashier creation with temporary pins and pin reset"
```
