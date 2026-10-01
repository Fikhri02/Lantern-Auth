# Lantern Auth — Plan 3: Till App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Blazor Web Till that does three things:
- a till is signed in once with its outlet account and stays registered across browser, server and Keycloak restarts
- cashiers identify themselves with a PIN, and are locked out after 10 idle minutes
- it rings sales whose receipts name the cashier and the till, re-checking the outlet login before every sale

**Architecture:**
- **Registration:** `Lantern.Till` is a server-rendered Blazor app (Interactive Server). Its OIDC handler runs the till's one-time outlet sign-in and never creates a cookie session. Instead, on the callback it:
  - stores the outlet's **offline refresh token**, encrypted with Data Protection, in SQLite on the data volume
  - sets a long-lived HttpOnly device cookie holding only a random device id
  - ends the online Keycloak session through RP-initiated logout
- **Session state:** a singleton `TillSessionService`, with one lock per device, keeps the outlet access token and the current cashier's tokens in memory. It talks to Keycloak (`KeycloakTillClient`) and to the API (`LanternApiClient`).
- **UI:** the components call that service directly.

**Tech Stack:** .NET 10 Blazor Web App (Interactive Server), `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.12, `Microsoft.Data.Sqlite` 10.0.12, ASP.NET Core Data Protection, `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 for idle tests. Tests use xUnit + Testcontainers, as in Plans 1–2.

**Spec:** `docs/superpowers/specs/2026-10-01-lantern-auth-design.md`, §6.1 steps 1–6 (till side), §6.2 steps 1–6 (till side), §6.5 including the paragraph added in Plan 2's review, and §7 (till rows). Roadmap: `docs/superpowers/plans/2026-10-01-roadmap.md`.

## Global Constraints

- Everything in Plans 1–2's Global Constraints still holds: versions, fictional setting, demo password `Lantern!2026`, conventional commits with **no trailers**, repo-local `user.email` `irfanfikhri@gmail.com`, and `dotnet` via `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`.
- Branch `irfan/till-app`, created from `irfan/design-spec`.
- **Till origin:** `http://localhost:5400`. Its OIDC callback is `/signin-oidc`, and its post-logout landing is `/signed-out`. Both are already covered by the `till` client's `http://localhost:5400/*` redirect and post-logout patterns.
- **Till client:** `till` with secret `till-dev-secret`. Scope `openid offline_access`. Response mode `query`, PKCE on.
- **Device cookie:** `lantern.till.device`. HttpOnly, SameSite=Lax, path `/`, expires in 400 days (the browser maximum). Its value is a 32-byte random id, base64url-encoded.
- **Idle lock:** 10 minutes (`Till:IdleMinutes`).
- **Outlet login check:** the outlet login is refreshed before every sale, and whenever the outlet access token is within 60 s of expiry. A failed refresh (anything except "Keycloak unreachable") deletes the registration and drops the cashier (spec §6.5).
- **Fail-safe direction:**
  - If Keycloak is unreachable, **sales are refused**.
  - If Keycloak is unreachable, **the registration is kept**, so a network blip must never unregister a till.
- **`Program` is namespaced** (`Lantern.Till.Program`, a classic `Main`). The test project references both web apps, and two top-level `Program` classes would clash.
- **Configuration is read lazily** through `IOptions<TillOptions>` everywhere, including the Data Protection key directory. `WebApplicationFactory` applies test configuration only when the host is built, after `Program` has registered services.

**Plan decisions (no spec change):**
1. The till's product buttons come from a small hard-coded `TillCatalog` (the same SKUs as the API's `DemoStore.Catalog`). The API remains the source of prices: the receipt shows what it charged.
2. UI behaviour is verified here through prerendered HTML and the services the components call. Clicking through it in a real browser is Plan 5 (Playwright).

## Review Focus

1. **Keycloak is unreachable when the till loads.** The till shows "Sign-in is temporarily unavailable" and **keeps its registration**. Test in Task 4.
2. **The till's Data Protection keys are lost** (for example, the volume is recreated). The stored registration becomes unreadable, so the till treats itself as not registered and asks to sign in again, with no crash. Test in Task 2.
3. **A sale is attempted after the 10-minute idle window but before the UI's 30-second poll has run.** The server refuses it as locked, so the idle rule never depends on the browser. Test in Task 5.
4. **A cashier code typed with spaces or capitals** (`"  C-1001 "`). It's trimmed and lower-cased, and sign-in works. Test in Task 4.
5. **Two PIN submissions for the same till at once** (double tap, two tabs). They are serialized, exactly one cashier ends up active, and the other's Keycloak session is ended. Test in Task 4.

---

## File Structure

```
src/Lantern.Till/                      # created from `dotnet new blazor --interactivity Server --empty`
  Lantern.Till.csproj                  # + OpenIdConnect, Data.Sqlite
  Program.cs                           # namespaced Program.Main
  appsettings.json  Properties/launchSettings.json  Dockerfile
  Services/TillOptions.cs              # config shape + RealmUrl
  Services/TillSetup.cs                # AddLanternTill: options, data protection, http clients, OIDC
  Services/DeviceCookie.cs             # read/write lantern.till.device
  Services/TillRegistrationStore.cs    # SQLite + Data Protection
  Services/RegistrationEndpoints.cs    # /register, /signed-out, OIDC OnTokenValidated
  Services/TillMessages.cs             # error code → user message (spec §7)
  Services/TillModels.cs               # records shared by services and components
  Services/JwtPayload.cs               # read claims from a token Keycloak just issued
  Services/KeycloakTillClient.cs       # refresh, PIN grant, revoke, logout
  Services/LanternApiClient.cs         # POST /outlets/{id}/sales
  Services/TillSessionService.cs       # per-device state, outlet check, cashier, idle, sales
  Components/_Imports.razor            # + usings
  Components/Pages/Home.razor          # static page: reads device cookie, renders TillScreen interactive
  Components/Till/TillScreen.razor     # status-driven screen, 30 s poll
  Components/Till/PinPad.razor
  Components/Till/SaleScreen.razor
  Components/Till/TillCatalog.cs
  wwwroot/app.css                      # replaced: simple kiosk styles
tests/Lantern.IntegrationTests/
  Lantern.IntegrationTests.csproj      # + Lantern.Till reference, TimeProvider.Testing
  Infrastructure/LocalhostCookieJar.cs # extracted from OidcBrowser, cookies per origin
  Infrastructure/OidcBrowser.cs        # uses LocalhostCookieJar
  Infrastructure/TillFactory.cs        # WebApplicationFactory<Lantern.Till.Program>
  Infrastructure/TillBrowser.cs        # routes localhost:5400 → TestServer, rest → Keycloak
  Infrastructure/KeycloakFixture.cs    # + session-count helpers
  TillMessagesTests.cs  TillRegistrationStoreTests.cs  TillRegistrationTests.cs
  TillSessionTests.cs  TillSaleTests.cs  TillPageTests.cs
docker-compose.yml  scripts/smoke.sh  README.md  .gitignore
```

---

### Task 1: Till project skeleton and user-facing messages

**Files:**
- Create: `src/Lantern.Till/` (from the template), `src/Lantern.Till/Services/TillMessages.cs`
- Modify: `src/Lantern.Till/Program.cs`, `src/Lantern.Till/Lantern.Till.csproj`, `src/Lantern.Till/Properties/launchSettings.json`, `tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`, `Lantern.slnx`, `.gitignore`
- Test: `tests/Lantern.IntegrationTests/TillMessagesTests.cs`

**Interfaces:**
- Produces:
  - `Lantern.Till.Program` (public class with `Main`).
  - `TillMessages.For(string? code)` → `string`.
  - `TillMessages.Unavailable = "keycloak_unavailable"`.

- [ ] **Step 1: Create the branch and the project**

```bash
cd ~/projects/Personal/DotNet/lantern-auth
git checkout -b irfan/till-app irfan/design-spec
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH
dotnet new blazor -n Lantern.Till -o src/Lantern.Till --interactivity Server --empty
rm src/Lantern.Till/appsettings.Development.json
dotnet sln add src/Lantern.Till/Lantern.Till.csproj
```

- [ ] **Step 2: Make `Program` namespaced, wire the packages, and reference the till from tests**

`src/Lantern.Till/Lantern.Till.csproj` (replace):
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.12" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.12" />
  </ItemGroup>
</Project>
```

`src/Lantern.Till/Program.cs` (replace; Task 3 adds the till services and endpoints):
```csharp
using Lantern.Till.Components;

namespace Lantern.Till;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.Run();
    }
}
```

`src/Lantern.Till/Properties/launchSettings.json` (replace):
```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5400",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

`tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`: add to the package `ItemGroup`:
```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
```
and to the project-reference `ItemGroup`:
```xml
    <ProjectReference Include="..\..\src\Lantern.Till\Lantern.Till.csproj" />
```

`.gitignore`: append:
```gitignore
src/Lantern.Till/data/
```

- [ ] **Step 3: Write the failing test**

`tests/Lantern.IntegrationTests/TillMessagesTests.cs`:
```csharp
using Lantern.Till.Services;

namespace Lantern.IntegrationTests;

public sealed class TillMessagesTests
{
    [Theory]
    [InlineData("pin_invalid:4", "Wrong PIN. 4 tries left.")]
    [InlineData("pin_invalid:1", "Wrong PIN. 1 try left.")]
    [InlineData("pin_invalid:0", "Wrong PIN. No tries left.")]
    [InlineData("pin_locked", "Too many wrong PINs. Try again in 15 minutes or ask a manager.")]
    [InlineData("pin_missing", "Enter your PIN.")]
    [InlineData("pin_change_required", "Choose a new PIN to replace your temporary one.")]
    [InlineData("pin_rule_format", "Use 4 to 6 digits.")]
    [InlineData("pin_rule_same_as_temporary", "Your new PIN can't be the temporary one.")]
    [InlineData("pin_rule_repeated_digit", "Don't use the same digit throughout.")]
    [InlineData("pin_rule_sequence", "Don't use a simple sequence like 1234.")]
    [InlineData("cashier_not_found", "That cashier code isn't recognised.")]
    [InlineData("cashier_wrong_outlet", "This cashier isn't assigned to this outlet.")]
    [InlineData("cashier_disabled", "Your account has been deactivated.")]
    [InlineData("outlet_session_invalid", "This till has been signed out. Sign in with the till account again.")]
    [InlineData("keycloak_unavailable", "Sign-in is temporarily unavailable.")]
    [InlineData("something_new", "Sign-in failed. Try again.")]
    [InlineData("pin_invalid:x", "Sign-in failed. Try again.")]
    public void Codes_map_to_plain_messages(string code, string expected) =>
        Assert.Equal(expected, TillMessages.For(code));
}
```

- [ ] **Step 4: Run the test and confirm it fails**

Run: `dotnet test --filter FullyQualifiedName~TillMessagesTests`
Expected: FAIL, with a compilation error: the type or namespace `Lantern.Till.Services` does not exist.

- [ ] **Step 5: Implement**

`src/Lantern.Till/Services/TillMessages.cs`:
```csharp
namespace Lantern.Till.Services;

/// <summary>Maps Keycloak's till error codes to what the cashier reads (spec §7). Never shows a raw code.</summary>
public static class TillMessages
{
    public const string Unavailable = "keycloak_unavailable";

    public static string For(string? code) => code switch
    {
        null or "" => "",
        "pin_missing" => "Enter your PIN.",
        "pin_locked" => "Too many wrong PINs. Try again in 15 minutes or ask a manager.",
        "pin_change_required" => "Choose a new PIN to replace your temporary one.",
        "pin_rule_format" => "Use 4 to 6 digits.",
        "pin_rule_same_as_temporary" => "Your new PIN can't be the temporary one.",
        "pin_rule_repeated_digit" => "Don't use the same digit throughout.",
        "pin_rule_sequence" => "Don't use a simple sequence like 1234.",
        "cashier_not_found" => "That cashier code isn't recognised.",
        "cashier_wrong_outlet" => "This cashier isn't assigned to this outlet.",
        "cashier_disabled" => "Your account has been deactivated.",
        "outlet_session_invalid" => "This till has been signed out. Sign in with the till account again.",
        Unavailable => "Sign-in is temporarily unavailable.",
        _ when code.StartsWith("pin_invalid:", StringComparison.Ordinal)
               && int.TryParse(code["pin_invalid:".Length..], out var left) => left switch
        {
            0 => "Wrong PIN. No tries left.",
            1 => "Wrong PIN. 1 try left.",
            _ => $"Wrong PIN. {left} tries left."
        },
        _ => "Sign-in failed. Try again."
    };
}
```

- [ ] **Step 6: Run the test and confirm it passes**

Run: `dotnet test --filter FullyQualifiedName~TillMessagesTests`
Expected: PASS (17 tests).
Then run: `dotnet test`
Expected: all pass. Referencing a second web app must not break the API tests.

- [ ] **Step 7: Commit**

```bash
git add .gitignore Lantern.slnx src/Lantern.Till tests
git commit -m "chore: scaffold till blazor app with user-facing pin messages"
```

---

### Task 2: Encrypted till registration store

**Files:**
- Create: `src/Lantern.Till/Services/TillOptions.cs`, `src/Lantern.Till/Services/TillModels.cs`, `src/Lantern.Till/Services/TillRegistrationStore.cs`, `src/Lantern.Till/appsettings.json` (replace)
- Test: `tests/Lantern.IntegrationTests/TillRegistrationStoreTests.cs`

**Interfaces:**
- Produces:
  - `TillOptions` with `KeycloakBaseUrl`, `Realm`, `Issuer`, `ClientId`, `ClientSecret`, `ApiBaseUrl`, `DataDirectory`, `IdleMinutes`, plus computed `RealmUrl`. Section name `"Till"`.
  - `TillRegistration(string DeviceId, string Account, string OutletId, string RefreshToken, DateTimeOffset RegisteredAt)`.
  - `TillRegistrationStore(IDataProtectionProvider, IOptions<TillOptions>)`:
    - `SaveAsync(TillRegistration, ct)`
    - `FindAsync(string deviceId, ct)` → `TillRegistration?`. Returns null, and deletes the row, when it can't be decrypted.
    - `UpdateRefreshTokenAsync(string deviceId, string refreshToken, ct)`
    - `DeleteAsync(string deviceId, ct)`
    - `DatabasePath`
  - Only the refresh token is encrypted. Device id, account and outlet are plain.

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/TillRegistrationStoreTests.cs`:
```csharp
using System.Text;
using Lantern.Till.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests;

public sealed class TillRegistrationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lantern-till-store-" + Guid.NewGuid().ToString("N"));

    private TillRegistrationStore Store(string keysFolder = "keys") => new(
        DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, keysFolder)), b => b.SetApplicationName("lantern-till")),
        Options.Create(new TillOptions { DataDirectory = _root }));

    private static TillRegistration Sample(string device = "device-1") =>
        new(device, "outlet-bangsar-1", "BGS", "offline.refresh.token-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Saved_registration_reads_back()
    {
        var store = Store();
        var registration = Sample();

        await store.SaveAsync(registration);
        var found = await store.FindAsync("device-1");

        Assert.Equal(registration.Account, found!.Account);
        Assert.Equal(registration.OutletId, found.OutletId);
        Assert.Equal(registration.RefreshToken, found.RefreshToken);
    }

    [Fact]
    public async Task Refresh_token_is_not_stored_in_plain_text()
    {
        var store = Store();
        var registration = Sample();

        await store.SaveAsync(registration);
        SqliteConnection.ClearAllPools();

        var raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(store.DatabasePath));
        Assert.DoesNotContain(registration.RefreshToken, raw);
        Assert.Contains(registration.Account, raw);
    }

    [Fact]
    public async Task Updating_the_refresh_token_replaces_it()
    {
        var store = Store();
        await store.SaveAsync(Sample());

        await store.UpdateRefreshTokenAsync("device-1", "rotated-token");

        Assert.Equal("rotated-token", (await store.FindAsync("device-1"))!.RefreshToken);
    }

    [Fact]
    public async Task Deleted_registration_is_gone()
    {
        var store = Store();
        await store.SaveAsync(Sample());

        await store.DeleteAsync("device-1");

        Assert.Null(await store.FindAsync("device-1"));
    }

    [Fact]
    public async Task Unknown_device_is_not_registered()
    {
        Assert.Null(await Store().FindAsync("nobody"));
    }

    [Fact]
    public async Task Registration_unreadable_after_key_loss_is_treated_as_not_registered()
    {
        await Store("keys-original").SaveAsync(Sample());

        var afterKeyLoss = Store("keys-recreated");

        Assert.Null(await afterKeyLoss.FindAsync("device-1"));
        Assert.Null(await Store("keys-original").FindAsync("device-1")); // the unreadable row was removed
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~TillRegistrationStoreTests`
Expected: FAIL, with compilation errors: `TillRegistrationStore`, `TillOptions` and `TillRegistration` are not found.

- [ ] **Step 3: Implement**

`src/Lantern.Till/Services/TillOptions.cs`:
```csharp
namespace Lantern.Till.Services;

public sealed class TillOptions
{
    public const string Section = "Till";

    /// <summary>Back-channel base URL for Keycloak (e.g. http://keycloak:8080 inside Docker).</summary>
    public string KeycloakBaseUrl { get; set; } = "http://localhost:8080";
    public string Realm { get; set; } = "lantern";
    /// <summary>Exact iss in tokens; differs from KeycloakBaseUrl inside Docker (spec §8).</summary>
    public string Issuer { get; set; } = "http://localhost:8080/realms/lantern";
    public string ClientId { get; set; } = "till";
    public string ClientSecret { get; set; } = "till-dev-secret";
    public string ApiBaseUrl { get; set; } = "http://localhost:5100";
    /// <summary>Holds till.db and the Data Protection keys; a Docker volume in compose.</summary>
    public string DataDirectory { get; set; } = "data";
    public int IdleMinutes { get; set; } = 10;

    public string RealmUrl => $"{KeycloakBaseUrl.TrimEnd('/')}/realms/{Realm}";
}
```

`src/Lantern.Till/Services/TillModels.cs`:
```csharp
namespace Lantern.Till.Services;

/// <summary>A till signed in as an outlet account (spec §6.1). RefreshToken is the offline token.</summary>
public sealed record TillRegistration(string DeviceId, string Account, string OutletId, string RefreshToken, DateTimeOffset RegisteredAt);
```

`src/Lantern.Till/Services/TillRegistrationStore.cs`:
```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Lantern.Till.Services;

/// <summary>
/// Till registrations in SQLite on the data volume. The offline refresh token is encrypted with Data
/// Protection; if the keys are lost the row can't be read, and the till asks to be signed in again.
/// </summary>
public sealed class TillRegistrationStore
{
    private readonly IDataProtector _protector;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public TillRegistrationStore(IDataProtectionProvider dataProtection, IOptions<TillOptions> options)
    {
        _protector = dataProtection.CreateProtector("Lantern.Till.Registration.v1");
        Directory.CreateDirectory(options.Value.DataDirectory);
        DatabasePath = Path.Combine(options.Value.DataDirectory, "till.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
    }

    public string DatabasePath { get; }

    public async Task SaveAsync(TillRegistration registration, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO registrations (device_id, account, outlet_id, refresh_token, registered_at)
            VALUES ($device, $account, $outlet, $token, $at)
            """;
        cmd.Parameters.AddWithValue("$device", registration.DeviceId);
        cmd.Parameters.AddWithValue("$account", registration.Account);
        cmd.Parameters.AddWithValue("$outlet", registration.OutletId);
        cmd.Parameters.AddWithValue("$token", _protector.Protect(registration.RefreshToken));
        cmd.Parameters.AddWithValue("$at", registration.RegisteredAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TillRegistration?> FindAsync(string deviceId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT account, outlet_id, refresh_token, registered_at FROM registrations WHERE device_id = $device";
        cmd.Parameters.AddWithValue("$device", deviceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var (account, outletId, protectedToken, registeredAt) =
            (reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)));
        await reader.DisposeAsync();
        try
        {
            return new TillRegistration(deviceId, account, outletId, _protector.Unprotect(protectedToken), registeredAt);
        }
        catch (CryptographicException)
        {
            await DeleteAsync(deviceId, ct);
            return null;
        }
    }

    public async Task UpdateRefreshTokenAsync(string deviceId, string refreshToken, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE registrations SET refresh_token = $token WHERE device_id = $device";
        cmd.Parameters.AddWithValue("$token", _protector.Protect(refreshToken));
        cmd.Parameters.AddWithValue("$device", deviceId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string deviceId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM registrations WHERE device_id = $device";
        cmd.Parameters.AddWithValue("$device", deviceId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        if (_schemaReady) return db;

        await _schemaLock.WaitAsync(ct);
        try
        {
            var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS registrations (
                    device_id     TEXT PRIMARY KEY,
                    account       TEXT NOT NULL,
                    outlet_id     TEXT NOT NULL,
                    refresh_token TEXT NOT NULL,
                    registered_at TEXT NOT NULL)
                """;
            await cmd.ExecuteNonQueryAsync(ct);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
        return db;
    }
}
```

`src/Lantern.Till/appsettings.json` (replace):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Till": {
    "KeycloakBaseUrl": "http://localhost:8080",
    "Realm": "lantern",
    "Issuer": "http://localhost:8080/realms/lantern",
    "ClientId": "till",
    "ClientSecret": "till-dev-secret",
    "ApiBaseUrl": "http://localhost:5100",
    "DataDirectory": "data",
    "IdleMinutes": 10
  }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~TillRegistrationStoreTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Lantern.Till tests
git commit -m "feat: encrypted sqlite store for till registrations"
```

---

### Task 3: Registering a till (OIDC sign-in, device cookie, ending the online session)

**Files:**
- Create: `src/Lantern.Till/Services/DeviceCookie.cs`, `src/Lantern.Till/Services/TillSetup.cs`, `src/Lantern.Till/Services/RegistrationEndpoints.cs`
- Create: `tests/Lantern.IntegrationTests/Infrastructure/LocalhostCookieJar.cs`, `Infrastructure/TillFactory.cs`, `Infrastructure/TillBrowser.cs`
- Modify: `src/Lantern.Till/Program.cs`, `tests/Lantern.IntegrationTests/Infrastructure/OidcBrowser.cs`, `tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`
- Test: `tests/Lantern.IntegrationTests/TillRegistrationTests.cs`

**Interfaces:**
- Consumes: `TillOptions`, `TillRegistrationStore`, `TillRegistration` (Task 2), and the fixture's `CreateTempTillAsync`, `AdminClientAsync`, `GetTillClientUuidAsync` (Plans 1–2).
- Produces:
  - `DeviceCookie.Name`, `DeviceCookie.Read(HttpRequest)` → `string?`, `DeviceCookie.Write(HttpResponse, string deviceId)`, `DeviceCookie.NewId()`.
  - `IServiceCollection.AddLanternTill(IConfiguration)`. This registers options, Data Protection (keys in `{DataDirectory}/keys`), `TimeProvider.System`, `TillRegistrationStore`, the named HttpClients `"keycloak"` and `"lantern-api"`, the cookie scheme `"till-signin"` (never actually signed into), and the OIDC scheme `"keycloak"`.
  - `IEndpointRouteBuilder.MapRegistrationEndpoints()`: `GET /register` and `GET /signed-out`.
  - Test infrastructure:
    - `LocalhostCookieJar(HttpMessageHandler inner)` keeps cookies per origin; `Get(string origin, string name)` → `string?`
    - `TillFactory(KeycloakFixture kc, string dataDirectory, Action<IServiceCollection>? configureServices = null)` with `Store`, plus `Session` from Task 4
    - `TillBrowser(TillFactory till)` with `RegisterAsync(string account, string password = "Lantern!2026")` → `BrowserPage(Uri Url, int Status, string Html)`, `GetAsync(string pathOrUrl)` → `BrowserPage`, and `DeviceId`
    - fixture `UserSessionCountAsync(string userId)` and `OfflineTillSessionCountAsync(string userId)` → `int`

- [ ] **Step 1: Extract the cookie jar and write the till test infrastructure**

`tests/Lantern.IntegrationTests/Infrastructure/LocalhostCookieJar.cs`:
```csharp
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
```

`tests/Lantern.IntegrationTests/Infrastructure/OidcBrowser.cs`:
- replace the `_http` field initializer with:
```csharp
    private readonly HttpClient _http = new(new LocalhostCookieJar(new HttpClientHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false
    }));
```
- delete the nested `private sealed class LocalhostCookieJar … { … }` at the bottom of the file.

`tests/Lantern.IntegrationTests/Infrastructure/TillFactory.cs`:
```csharp
using Lantern.Till.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>The Till app in memory, pointed at the test Keycloak and the in-memory API.</summary>
public sealed class TillFactory(KeycloakFixture kc, string dataDirectory, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Lantern.Till.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Till:KeycloakBaseUrl"] = kc.BaseUrl,
            ["Till:Issuer"] = kc.Issuer,
            ["Till:ApiBaseUrl"] = "http://lantern-api",
            ["Till:DataDirectory"] = dataDirectory
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient("lantern-api").ConfigurePrimaryHttpMessageHandler(() => kc.Api.Server.CreateHandler());
            configureServices?.Invoke(services);
        });
    }

    public TillRegistrationStore Store => Services.GetRequiredService<TillRegistrationStore>();
}
```

`tests/Lantern.IntegrationTests/Infrastructure/TillBrowser.cs`:
```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace Lantern.IntegrationTests.Infrastructure;

public sealed record BrowserPage(Uri Url, int Status, string Html);

/// <summary>
/// A browser for the till: requests to http://localhost:5400 go to the in-memory Till, everything else
/// (Keycloak) goes over the network. Follows redirects across both, keeping cookies per origin.
/// </summary>
public sealed partial class TillBrowser : IDisposable
{
    public const string TillOrigin = "http://localhost:5400";

    private readonly LocalhostCookieJar _jar;
    private readonly HttpClient _http;

    public TillBrowser(TillFactory till)
    {
        _jar = new LocalhostCookieJar(new OriginRouter(till.Server.CreateHandler(),
            new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }));
        _http = new HttpClient(_jar);
    }

    public string? DeviceId => _jar.Get(TillOrigin, "lantern.till.device");

    public Task<BrowserPage> GetAsync(string pathOrUrl) =>
        FollowAsync(new HttpRequestMessage(HttpMethod.Get, Absolute(pathOrUrl)));

    /// <summary>Clicks "Sign in this till", fills in Keycloak's form, and follows every redirect back.</summary>
    public async Task<BrowserPage> RegisterAsync(string account, string password = KeycloakFixture.DemoPassword)
    {
        var login = await GetAsync("/register");
        var action = LoginAction(login.Html)
                     ?? throw new InvalidOperationException($"Expected Keycloak's login form at {login.Url}, got {login.Status}");
        return await FollowAsync(new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = account, ["password"] = password, ["credentialId"] = ""
            })
        });
    }

    public void Dispose() => _http.Dispose();

    private async Task<BrowserPage> FollowAsync(HttpRequestMessage request)
    {
        for (var hop = 0; hop < 20; hop++)
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

    private static Uri Absolute(string pathOrUrl) =>
        Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute) ? absolute : new Uri(new Uri(TillOrigin), pathOrUrl);

    [GeneratedRegex("<form\\b[^>]*\\bid=\"kc-form-login\"[^>]*>")]
    private static partial Regex LoginFormTag();

    [GeneratedRegex("\\baction=\"([^\"]+)\"")]
    private static partial Regex ActionAttribute();

    private static Uri? LoginAction(string html)
    {
        var tag = LoginFormTag().Match(html);
        if (!tag.Success) return null;
        var action = ActionAttribute().Match(tag.Value);
        return action.Success ? new Uri(WebUtility.HtmlDecode(action.Groups[1].Value)) : null;
    }

    private sealed class OriginRouter(HttpMessageHandler till, HttpMessageHandler network) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _till = new(till);
        private readonly HttpMessageInvoker _network = new(network);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            request.RequestUri!.GetLeftPart(UriPartial.Authority) == TillOrigin
                ? _till.SendAsync(request, ct)
                : _network.SendAsync(request, ct);
    }
}
```

`tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`: add after `CashierTokenAsync`:
```csharp
    public async Task<int> UserSessionCountAsync(string userId)
    {
        using var admin = await AdminClientAsync();
        return (await admin.GetFromJsonAsync<JsonElement>($"users/{userId}/sessions")).GetArrayLength();
    }

    public async Task<int> OfflineTillSessionCountAsync(string userId)
    {
        using var admin = await AdminClientAsync();
        return (await admin.GetFromJsonAsync<JsonElement>($"users/{userId}/offline-sessions/{await GetTillClientUuidAsync()}")).GetArrayLength();
    }
```

- [ ] **Step 2: Write the failing tests**

`tests/Lantern.IntegrationTests/TillRegistrationTests.cs`:
```csharp
using System.Net;
using System.Text;
using Lantern.IntegrationTests.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.1 steps 1–6, from the till's side.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class TillRegistrationTests(KeycloakFixture kc) : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "lantern-till-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Registering_stores_an_encrypted_registration_and_ends_the_online_session()
    {
        var (accountId, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await using var till = new TillFactory(kc, _dataDir);
        using var browser = new TillBrowser(till);

        var landed = await browser.RegisterAsync(account);

        Assert.Equal(TillBrowser.TillOrigin + "/", landed.Url.ToString());
        Assert.Equal(200, landed.Status);
        var registration = await till.Store.FindAsync(browser.DeviceId!);
        Assert.Equal(account, registration!.Account);
        Assert.Equal("BGS", registration.OutletId);

        SqliteConnection.ClearAllPools();
        Assert.DoesNotContain(registration.RefreshToken, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(till.Store.DatabasePath)));

        Assert.Equal(0, await kc.UserSessionCountAsync(accountId));
        Assert.Equal(1, await kc.OfflineTillSessionCountAsync(accountId));
    }

    [Fact]
    public async Task Registration_survives_restarting_the_till()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        string deviceId;
        await using (var first = new TillFactory(kc, _dataDir))
        {
            using var browser = new TillBrowser(first);
            await browser.RegisterAsync(account);
            deviceId = browser.DeviceId!;
        }

        await using var restarted = new TillFactory(kc, _dataDir);

        Assert.Equal(account, (await restarted.Store.FindAsync(deviceId))!.Account);
    }

    [Fact]
    public async Task A_second_till_with_the_same_account_is_refused_and_not_registered()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await using var till = new TillFactory(kc, _dataDir);
        using var firstTill = new TillBrowser(till);
        using var secondTill = new TillBrowser(till);
        await firstTill.RegisterAsync(account);

        var refused = await secondTill.RegisterAsync(account);

        Assert.Contains("already signed in on another till", WebUtility.HtmlDecode(refused.Html));
        Assert.Null(secondTill.DeviceId);
    }

    [Fact]
    public async Task A_registered_till_going_to_register_again_stays_home()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        await using var till = new TillFactory(kc, _dataDir);
        using var browser = new TillBrowser(till);
        await browser.RegisterAsync(account);

        var page = await browser.GetAsync("/register");

        Assert.Equal(TillBrowser.TillOrigin + "/", page.Url.ToString());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~TillRegistrationTests`
Expected: FAIL at runtime. `/register` doesn't exist yet, so `RegisterAsync` throws "Expected Keycloak's login form … got 404", and `A_registered_till…` fails for the same reason.

- [ ] **Step 4: Implement the device cookie, the setup and the endpoints**

`src/Lantern.Till/Services/DeviceCookie.cs`:
```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Lantern.Till.Services;

/// <summary>Long-lived cookie naming this browser's till registration. Holds only a random id (spec §6.1).</summary>
public static class DeviceCookie
{
    public const string Name = "lantern.till.device";

    public static string NewId() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static void Write(HttpResponse response, string deviceId) =>
        response.Cookies.Append(Name, deviceId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(400),
            IsEssential = true
        });
}
```

`src/Lantern.Till/Services/TillSetup.cs`:
```csharp
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Lantern.Till.Services;

public static class TillSetup
{
    public const string SignInScheme = "till-signin";
    public const string KeycloakScheme = "keycloak";

    public static IServiceCollection AddLanternTill(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TillOptions>(config.GetSection(TillOptions.Section));

        services.AddDataProtection().SetApplicationName("lantern-till");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<TillOptions>, ILoggerFactory>((keys, till, logs) =>
                keys.XmlRepository = new FileSystemXmlRepository(
                    new DirectoryInfo(Path.Combine(till.Value.DataDirectory, "keys")), logs));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TillRegistrationStore>();
        services.AddHttpClient("keycloak", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient("lantern-api", c => c.Timeout = TimeSpan.FromSeconds(10));

        // The OIDC handler is used only for the one-time outlet sign-in; RegistrationEndpoints.OnTokenValidated
        // stores the registration and ends the response, so the till never holds a cookie session.
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = SignInScheme;
                o.DefaultChallengeScheme = KeycloakScheme;
            })
            .AddCookie(SignInScheme, o => o.Cookie.Name = "lantern.till.signin")
            .AddOpenIdConnect(KeycloakScheme, _ => { });

        services.AddOptions<OpenIdConnectOptions>(KeycloakScheme)
            .Configure<IOptions<TillOptions>>((o, tillOptions) =>
            {
                var till = tillOptions.Value;
                o.MetadataAddress = $"{till.RealmUrl}/.well-known/openid-configuration";
                o.RequireHttpsMetadata = false;
                o.ClientId = till.ClientId;
                o.ClientSecret = till.ClientSecret;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.ResponseMode = OpenIdConnectResponseMode.Query;
                o.UsePkce = true;
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("offline_access");
                o.SaveTokens = false;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.TokenValidationParameters.ValidIssuer = till.Issuer;
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.CallbackPath = "/signin-oidc";
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.NonceCookie.SameSite = SameSiteMode.Lax;
                o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnTokenValidated = RegistrationEndpoints.OnTokenValidatedAsync;
                o.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/?signin=failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        return services;
    }
}
```

`src/Lantern.Till/Services/RegistrationEndpoints.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Lantern.Till.Services;

/// <summary>Spec §6.1 steps 1–5: one-time outlet sign-in, encrypted registration, device cookie, end online session.</summary>
public static class RegistrationEndpoints
{
    public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/register", async (HttpContext http, TillRegistrationStore store, CancellationToken ct) =>
        {
            var deviceId = DeviceCookie.Read(http.Request);
            if (deviceId is not null && await store.FindAsync(deviceId, ct) is not null) return Results.Redirect("/");
            return Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [TillSetup.KeycloakScheme]);
        });

        app.MapGet("/signed-out", () => Results.Redirect("/"));
    }

    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var http = context.HttpContext;
        var tokens = context.TokenEndpointResponse;
        var account = context.Principal?.FindFirstValue("preferred_username");
        var outletId = context.Principal?.FindFirstValue("outlet_id");
        if (tokens is null || string.IsNullOrEmpty(tokens.RefreshToken) || string.IsNullOrEmpty(tokens.IdToken) ||
            account is null || outletId is null)
        {
            context.Fail("The outlet sign-in did not return an offline login for an outlet account.");
            return;
        }

        var store = http.RequestServices.GetRequiredService<TillRegistrationStore>();
        var time = http.RequestServices.GetRequiredService<TimeProvider>();
        var deviceId = DeviceCookie.NewId();
        await store.SaveAsync(new TillRegistration(deviceId, account, outletId, tokens.RefreshToken, time.GetUtcNow()), http.RequestAborted);
        DeviceCookie.Write(http.Response, deviceId);

        // End the online session so the till's browser keeps no Keycloak sign-in (spec §6.1 step 5).
        var configuration = await context.Options.ConfigurationManager!.GetConfigurationAsync(http.RequestAborted);
        var signedOut = $"{http.Request.Scheme}://{http.Request.Host}/signed-out";
        context.Response.Redirect($"{configuration.EndSessionEndpoint}?id_token_hint={Uri.EscapeDataString(tokens.IdToken)}" +
                                  $"&post_logout_redirect_uri={Uri.EscapeDataString(signedOut)}");
        context.HandleResponse();
    }
}
```

`src/Lantern.Till/Program.cs`:
- add `using Lantern.Till.Services;` after `using Lantern.Till.Components;`
- after `builder.Services.AddRazorComponents().AddInteractiveServerComponents();` add:
```csharp
        builder.Services.AddLanternTill(builder.Configuration);
```
- replace `app.UseAntiforgery();` with:
```csharp
        app.UseAuthentication();
        app.UseAntiforgery();
        app.MapRegistrationEndpoints();
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~TillRegistrationTests`
Expected: PASS (4 tests). If registration lands on `/?signin=failed`, the OIDC callback failed. Check, in this order:
1. Is the correlation cookie present on the `/signin-oidc` request? The `TillBrowser` jar must hold `.AspNetCore.Correlation.*` for `http://localhost:5400`.
2. Does the id token's issuer equal `Till:Issuer`?

Then run: `dotnet test`
Expected: all pass, including `OidcBrowser` users after the jar extraction.

- [ ] **Step 6: Commit**

```bash
git add src/Lantern.Till tests
git commit -m "feat: till registration via outlet sign-in with device cookie"
```

---

### Task 4: Till session service (outlet check, cashier PIN sign-in, idle lock, sign out)

**Files:**
- Create: `src/Lantern.Till/Services/JwtPayload.cs`, `src/Lantern.Till/Services/KeycloakTillClient.cs`, `src/Lantern.Till/Services/TillSessionService.cs`
- Modify: `src/Lantern.Till/Services/TillModels.cs`, `src/Lantern.Till/Services/TillSetup.cs`, `tests/Lantern.IntegrationTests/Infrastructure/TillFactory.cs`
- Test: `tests/Lantern.IntegrationTests/TillSessionTests.cs`

**Interfaces:**
- Consumes: `TillRegistrationStore`, `TillOptions`, `TillMessages`, named HttpClient `"keycloak"`, and `TimeProvider` (Tasks 1–3).
- Produces:
  - Models:
    - `TillMode { NotRegistered, Locked, Active }`
    - `ActiveCashier(string Code, string Name)`
    - `TillStatus(TillMode Mode, string? OutletId, string? TillAccount, ActiveCashier? Cashier, bool KeycloakUnavailable)`, with static `TillStatus.NotRegistered`
    - `PinAttempt(string Code, string Pin, string? NewPin)`
    - `PinResult(bool Succeeded, string? ErrorCode, string Message, bool ChangePinRequired, bool TillSignedOut)`
    - `TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn)`
  - `KeycloakTillClient(IHttpClientFactory, IOptions<TillOptions>)`:
    - `RefreshAsync(refreshToken, ct)` → `(TokenResponse? Tokens, string? Error)`
    - `CashierPinAsync(outletAccessToken, code, pin, newPin, ct)` → same tuple
    - `RevokeAsync(refreshToken, ct)` and `LogoutAsync(refreshToken, ct)`, both best-effort
    - `Error` is Keycloak's `error_description`, or `TillMessages.Unavailable` when Keycloak can't be reached
  - `TillSessionService` (singleton):
    - `GetStatusAsync(string? deviceId, ct)` → `TillStatus`
    - `SignInCashierAsync(string deviceId, PinAttempt attempt, ct)` → `PinResult`
    - `LockAsync(string deviceId, ct)`
    - `SignOutTillAsync(string deviceId, ct)`
    - `RingSaleAsync` comes in Task 5
  - `JwtPayload.Read(string jwt)` → `JsonElement`.
  - `TillFactory.Session`.

- [ ] **Step 1: Add `Session` to the factory and write the failing tests**

`tests/Lantern.IntegrationTests/Infrastructure/TillFactory.cs`: add below `Store`:
```csharp
    public TillSessionService Session => Services.GetRequiredService<TillSessionService>();
```

`tests/Lantern.IntegrationTests/TillSessionTests.cs`:
```csharp
using Lantern.IntegrationTests.Infrastructure;
using Lantern.Till.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Lantern.IntegrationTests;

/// <summary>Spec §6.2 and §6.5, from the till's side: cashier PIN sign-in, idle lock, sign out.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class TillSessionTests(KeycloakFixture kc) : IAsyncLifetime
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "lantern-till-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private TillFactory _till = null!;

    public Task InitializeAsync()
    {
        _till = new TillFactory(kc, _dataDir, s => s.AddSingleton<TimeProvider>(_time));
        return Task.CompletedTask;
    }

    private async Task<(string DeviceId, string AccountId, string Account)> RegisterAsync(string outletGroup = "/Outlets/Bangsar")
    {
        var (accountId, account) = await kc.CreateTempTillAsync(outletGroup);
        using var browser = new TillBrowser(_till);
        await browser.RegisterAsync(account);
        return (browser.DeviceId!, accountId, account);
    }

    [Fact]
    public async Task Unknown_device_is_not_registered()
    {
        Assert.Equal(TillMode.NotRegistered, (await _till.Session.GetStatusAsync("no-such-device")).Mode);
        Assert.Equal(TillMode.NotRegistered, (await _till.Session.GetStatusAsync(null)).Mode);
    }

    [Fact]
    public async Task Registered_till_waits_on_the_pin_pad()
    {
        var (device, _, account) = await RegisterAsync();

        var status = await _till.Session.GetStatusAsync(device);

        Assert.Equal(TillMode.Locked, status.Mode);
        Assert.Equal(account, status.TillAccount);
        Assert.Equal("BGS", status.OutletId);
    }

    [Fact]
    public async Task Cashier_signs_in_and_the_till_shows_who_is_serving()
    {
        var (device, _, _) = await RegisterAsync();

        var result = await _till.Session.SignInCashierAsync(device, new PinAttempt("c-1001", "1111", null));

        Assert.True(result.Succeeded);
        var status = await _till.Session.GetStatusAsync(device);
        Assert.Equal(TillMode.Active, status.Mode);
        Assert.Equal(new ActiveCashier("c-1001", "Siti Aminah"), status.Cashier);
    }

    [Fact]
    public async Task Cashier_code_with_spaces_and_capitals_still_works()
    {
        var (device, _, _) = await RegisterAsync();
        Assert.True((await _till.Session.SignInCashierAsync(device, new PinAttempt("  C-1002 ", "2222", null))).Succeeded);
    }

    [Fact]
    public async Task Wrong_pin_reports_tries_left_in_plain_words()
    {
        var (device, _, _) = await RegisterAsync();
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");

        var result = await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "0000", null));

        Assert.False(result.Succeeded);
        Assert.Equal("Wrong PIN. 4 tries left.", result.Message);
    }

    [Fact]
    public async Task Temporary_pin_asks_for_a_new_one_then_signs_in()
    {
        var (device, _, _) = await RegisterAsync();
        var (_, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "864213", temporary: true);

        var first = await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "864213", null));
        var changed = await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "864213", "2580"));

        Assert.True(first.ChangePinRequired);
        Assert.True(changed.Succeeded);
    }

    [Fact]
    public async Task Next_cashier_replaces_the_current_one_and_ends_their_session()
    {
        var (device, _, _) = await RegisterAsync();
        var (firstId, first) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        var (_, second) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "1357");
        await _till.Session.SignInCashierAsync(device, new PinAttempt(first, "2468", null));

        await _till.Session.SignInCashierAsync(device, new PinAttempt(second, "1357", null));

        Assert.Equal(second, (await _till.Session.GetStatusAsync(device)).Cashier!.Code);
        Assert.Equal(0, await kc.UserSessionCountAsync(firstId));
    }

    [Fact]
    public async Task Two_pin_submissions_at_once_leave_exactly_one_cashier()
    {
        var (device, _, _) = await RegisterAsync();
        var (aId, a) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        var (bId, b) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "1357");

        await Task.WhenAll(
            _till.Session.SignInCashierAsync(device, new PinAttempt(a, "2468", null)),
            _till.Session.SignInCashierAsync(device, new PinAttempt(b, "1357", null)));

        var active = (await _till.Session.GetStatusAsync(device)).Cashier!.Code;
        var otherId = active == a ? bId : aId;
        Assert.Contains(active, new[] { a, b });
        Assert.Equal(0, await kc.UserSessionCountAsync(otherId));
    }

    [Fact]
    public async Task Idle_cashier_is_locked_out_after_ten_minutes()
    {
        var (device, _, _) = await RegisterAsync();
        var (cashierId, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "2468", null));

        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var status = await _till.Session.GetStatusAsync(device);

        Assert.Equal(TillMode.Locked, status.Mode);
        Assert.Equal(0, await kc.UserSessionCountAsync(cashierId));
    }

    [Fact]
    public async Task Lock_ends_the_cashier_session()
    {
        var (device, _, _) = await RegisterAsync();
        var (cashierId, cashier) = await kc.CreateTempCashierAsync("/Outlets/Bangsar", "2468");
        await _till.Session.SignInCashierAsync(device, new PinAttempt(cashier, "2468", null));

        await _till.Session.LockAsync(device);

        Assert.Equal(TillMode.Locked, (await _till.Session.GetStatusAsync(device)).Mode);
        Assert.Equal(0, await kc.UserSessionCountAsync(cashierId));
    }

    [Fact]
    public async Task Released_till_shows_as_signed_out_and_forgets_its_registration()
    {
        var (device, accountId, _) = await RegisterAsync();

        await kc.RevokeTillLoginAsync(accountId);
        _time.Advance(TimeSpan.FromMinutes(6)); // outlet access token now expired, forcing a refresh

        Assert.Equal(TillMode.NotRegistered, (await _till.Session.GetStatusAsync(device)).Mode);
        Assert.Null(await _till.Store.FindAsync(device));
    }

    [Fact]
    public async Task Sign_out_this_till_frees_the_account_for_another_till()
    {
        var (device, _, account) = await RegisterAsync();

        await _till.Session.SignOutTillAsync(device);

        Assert.Null(await _till.Store.FindAsync(device));
        await kc.OutletLoginAsync(account); // would throw if the account were still taken
    }

    [Fact]
    public async Task Unreachable_keycloak_keeps_the_registration_and_says_so()
    {
        var (device, _, account) = await RegisterAsync();
        await using var offline = new TillFactory(kc, _dataDir, s =>
            s.AddHttpClient("keycloak").ConfigurePrimaryHttpMessageHandler(() => new UnreachableHandler()));

        var status = await offline.Session.GetStatusAsync(device);

        Assert.True(status.KeycloakUnavailable);
        Assert.Equal(TillMode.Locked, status.Mode);
        Assert.Equal(account, (await offline.Store.FindAsync(device))!.Account);
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }

    public async Task DisposeAsync()
    {
        await _till.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~TillSessionTests`
Expected: FAIL, with compilation errors: `TillSessionService`, `TillMode`, `PinAttempt` and `ActiveCashier` are not found.

- [ ] **Step 3: Implement**

`src/Lantern.Till/Services/TillModels.cs`: append:
```csharp
public enum TillMode { NotRegistered, Locked, Active }

public sealed record ActiveCashier(string Code, string Name);

public sealed record TillStatus(TillMode Mode, string? OutletId, string? TillAccount, ActiveCashier? Cashier, bool KeycloakUnavailable)
{
    public static readonly TillStatus NotRegistered = new(TillMode.NotRegistered, null, null, null, false);
}

public sealed record PinAttempt(string Code, string Pin, string? NewPin);

public sealed record PinResult(bool Succeeded, string? ErrorCode, string Message, bool ChangePinRequired, bool TillSignedOut);

public sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);
```

`src/Lantern.Till/Services/JwtPayload.cs`:
```csharp
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Lantern.Till.Services;

/// <summary>Reads claims from a token Keycloak just returned to us over the back channel; no validation needed.</summary>
public static class JwtPayload
{
    public static JsonElement Read(string jwt) =>
        JsonDocument.Parse(WebEncoders.Base64UrlDecode(jwt.Split('.')[1])).RootElement.Clone();
}
```

`src/Lantern.Till/Services/KeycloakTillClient.cs`:
```csharp
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Lantern.Till.Services;

/// <summary>The till server's calls to Keycloak's token, revocation and logout endpoints, as the till client.</summary>
public sealed class KeycloakTillClient(IHttpClientFactory httpFactory, IOptions<TillOptions> options)
{
    private TillOptions Options => options.Value;
    private string Oidc => $"{Options.RealmUrl}/protocol/openid-connect";

    public Task<(TokenResponse? Tokens, string? Error)> RefreshAsync(string refreshToken, CancellationToken ct) =>
        TokenAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);

    /// <summary>The PIN request (spec §6.2 step 2), routed by Keycloak to the till-cashier-pin flow.</summary>
    public Task<(TokenResponse? Tokens, string? Error)> CashierPinAsync(string outletAccessToken, string code, string pin, string? newPin, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["scope"] = "openid",
            ["username"] = code,
            ["pin"] = pin,
            ["outlet_token"] = outletAccessToken
        };
        if (newPin is not null) form["new_pin"] = newPin;
        return TokenAsync(form, ct);
    }

    /// <summary>Revokes the outlet's offline login (Sign out this till). Best effort.</summary>
    public Task RevokeAsync(string refreshToken, CancellationToken ct) =>
        PostBestEffortAsync($"{Oidc}/revoke", new Dictionary<string, string>
        {
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token"
        }, ct);

    /// <summary>Ends a cashier's Keycloak session. Best effort.</summary>
    public Task LogoutAsync(string refreshToken, CancellationToken ct) =>
        PostBestEffortAsync($"{Oidc}/logout", new Dictionary<string, string> { ["refresh_token"] = refreshToken }, ct);

    private async Task<(TokenResponse? Tokens, string? Error)> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = Options.ClientId;
        form["client_secret"] = Options.ClientSecret;
        try
        {
            using var response = await httpFactory.CreateClient("keycloak")
                .PostAsync($"{Oidc}/token", new FormUrlEncodedContent(form), ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var body = json.RootElement;
            if (response.IsSuccessStatusCode)
                return (new TokenResponse(
                    body.GetProperty("access_token").GetString()!,
                    body.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
                    body.GetProperty("expires_in").GetInt32()), null);

            var error = body.TryGetProperty("error_description", out var description) ? description.GetString()
                : body.TryGetProperty("error", out var code) ? code.GetString()
                : null;
            return (null, error ?? "unknown_error");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException ||
                                  e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, TillMessages.Unavailable);
        }
    }

    private async Task PostBestEffortAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = Options.ClientId;
        form["client_secret"] = Options.ClientSecret;
        try
        {
            using var _ = await httpFactory.CreateClient("keycloak").PostAsync(url, new FormUrlEncodedContent(form), ct);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Nothing more the till can do; the session will expire on Keycloak's idle timeout.
        }
    }
}
```

`src/Lantern.Till/Services/TillSessionService.cs`:
```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Lantern.Till.Services;

/// <summary>
/// Per-device till state: the outlet access token and the current cashier, held in memory (spec §6.2, §6.5).
/// Every operation for a device runs under that device's lock, so double taps and second tabs are serialized.
/// </summary>
public sealed class TillSessionService(
    TillRegistrationStore store,
    KeycloakTillClient keycloak,
    IOptions<TillOptions> options,
    TimeProvider time)
{
    private enum OutletCheck { Ok, Unavailable, SignedOut }

    private sealed class CashierSession
    {
        public required string Code { get; init; }
        public required string Name { get; init; }
        public required string AccessToken { get; set; }
        public required string RefreshToken { get; set; }
        public DateTimeOffset AccessExpiresAt { get; set; }
        public DateTimeOffset LastActivity { get; set; }
    }

    private sealed class DeviceState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? OutletAccessToken { get; set; }
        public DateTimeOffset OutletExpiresAt { get; set; }
        public CashierSession? Cashier { get; set; }
    }

    private readonly ConcurrentDictionary<string, DeviceState> _devices = new();

    public async Task<TillStatus> GetStatusAsync(string? deviceId, CancellationToken ct = default)
    {
        if (deviceId is null) return TillStatus.NotRegistered;
        return await WithDeviceAsync(deviceId, async (state, registration) =>
        {
            var outlet = await EnsureOutletAsync(registration, state, force: false, ct);
            if (outlet == OutletCheck.SignedOut) return TillStatus.NotRegistered;
            await ExpireIdleCashierAsync(state, ct);
            return Status(registration, state, outlet == OutletCheck.Unavailable);
        }, TillStatus.NotRegistered, ct);
    }

    public async Task<PinResult> SignInCashierAsync(string deviceId, PinAttempt attempt, CancellationToken ct = default) =>
        await WithDeviceAsync(deviceId, async (state, registration) =>
        {
            var outlet = await EnsureOutletAsync(registration, state, force: false, ct);
            if (outlet == OutletCheck.SignedOut) return SignedOut();
            if (outlet == OutletCheck.Unavailable) return Failed(TillMessages.Unavailable);

            var code = attempt.Code.Trim().ToLowerInvariant();
            var (tokens, error) = await keycloak.CashierPinAsync(state.OutletAccessToken!, code, attempt.Pin, attempt.NewPin, ct);
            if (tokens is null)
            {
                if (error == "outlet_session_invalid")
                {
                    await ForgetRegistrationAsync(registration, state, ct);
                    return SignedOut();
                }
                return Failed(error);
            }

            await EndCashierAsync(state, ct);
            var claims = JwtPayload.Read(tokens.AccessToken);
            var now = time.GetUtcNow();
            state.Cashier = new CashierSession
            {
                Code = claims.GetProperty("preferred_username").GetString()!,
                Name = claims.TryGetProperty("name", out var name) ? name.GetString()! : code,
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken ?? "",
                AccessExpiresAt = now.AddSeconds(tokens.ExpiresIn),
                LastActivity = now
            };
            return new PinResult(true, null, "", false, false);
        }, SignedOut(), ct);

    public Task LockAsync(string deviceId, CancellationToken ct = default) =>
        WithDeviceAsync(deviceId, async (state, _) =>
        {
            await EndCashierAsync(state, ct);
            return true;
        }, false, ct);

    /// <summary>Spec §6.1: revoke the offline login and delete the registration, freeing the account.</summary>
    public Task SignOutTillAsync(string deviceId, CancellationToken ct = default) =>
        WithDeviceAsync(deviceId, async (state, registration) =>
        {
            await keycloak.RevokeAsync(registration.RefreshToken, ct);
            await ForgetRegistrationAsync(registration, state, ct);
            return true;
        }, false, ct);

    private async Task<T> WithDeviceAsync<T>(string deviceId, Func<DeviceState, TillRegistration, Task<T>> action,
        T whenNotRegistered, CancellationToken ct)
    {
        var state = _devices.GetOrAdd(deviceId, _ => new DeviceState());
        await state.Gate.WaitAsync(ct);
        try
        {
            var registration = await store.FindAsync(deviceId, ct);
            if (registration is null)
            {
                await EndCashierAsync(state, ct);
                state.OutletAccessToken = null;
                return whenNotRegistered;
            }
            return await action(state, registration);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>Spec §6.5: a failed outlet refresh signs the till out and drops the cashier; unreachable keeps both.</summary>
    private async Task<OutletCheck> EnsureOutletAsync(TillRegistration registration, DeviceState state, bool force, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (!force && state.OutletAccessToken is not null && state.OutletExpiresAt - now > TimeSpan.FromSeconds(60))
            return OutletCheck.Ok;

        var (tokens, error) = await keycloak.RefreshAsync(registration.RefreshToken, ct);
        if (tokens is not null)
        {
            state.OutletAccessToken = tokens.AccessToken;
            state.OutletExpiresAt = now.AddSeconds(tokens.ExpiresIn);
            if (tokens.RefreshToken is { } rotated && rotated != registration.RefreshToken)
                await store.UpdateRefreshTokenAsync(registration.DeviceId, rotated, ct);
            return OutletCheck.Ok;
        }

        if (error == TillMessages.Unavailable) return OutletCheck.Unavailable;
        await ForgetRegistrationAsync(registration, state, ct);
        return OutletCheck.SignedOut;
    }

    private async Task ExpireIdleCashierAsync(DeviceState state, CancellationToken ct)
    {
        if (state.Cashier is { } cashier &&
            time.GetUtcNow() - cashier.LastActivity >= TimeSpan.FromMinutes(options.Value.IdleMinutes))
            await EndCashierAsync(state, ct);
    }

    private async Task EndCashierAsync(DeviceState state, CancellationToken ct)
    {
        if (state.Cashier is not { } cashier) return;
        state.Cashier = null;
        if (cashier.RefreshToken.Length > 0) await keycloak.LogoutAsync(cashier.RefreshToken, ct);
    }

    private async Task ForgetRegistrationAsync(TillRegistration registration, DeviceState state, CancellationToken ct)
    {
        await EndCashierAsync(state, ct);
        state.OutletAccessToken = null;
        await store.DeleteAsync(registration.DeviceId, ct);
    }

    private static TillStatus Status(TillRegistration registration, DeviceState state, bool keycloakUnavailable) =>
        new(state.Cashier is null ? TillMode.Locked : TillMode.Active, registration.OutletId, registration.Account,
            state.Cashier is { } c ? new ActiveCashier(c.Code, c.Name) : null, keycloakUnavailable);

    private static PinResult SignedOut() =>
        new(false, "outlet_session_invalid", TillMessages.For("outlet_session_invalid"), false, true);

    private static PinResult Failed(string? error) =>
        new(false, error, TillMessages.For(error), error == "pin_change_required", false);
}
```

`src/Lantern.Till/Services/TillSetup.cs`: after `services.AddSingleton<TillRegistrationStore>();` add:
```csharp
        services.AddSingleton<KeycloakTillClient>();
        services.AddSingleton<TillSessionService>();
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~TillSessionTests`
Expected: PASS (13 tests). Specific failures point to specific problems:
- **`Idle_cashier…` sees the cashier still active:** the service isn't using the injected `TimeProvider`.
- **`Released_till…` stays Locked:** the advance didn't push the outlet token inside the 60 s refresh window. The access token lifetime is 300 s, so 6 minutes must force a refresh.

Then run: `dotnet test`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Lantern.Till tests
git commit -m "feat: till session service with cashier pin sign-in, idle lock and till sign-out"
```

---

### Task 5: Ringing sales from the till, re-checking the outlet first

**Files:**
- Create: `src/Lantern.Till/Services/LanternApiClient.cs`
- Modify: `src/Lantern.Till/Services/TillModels.cs`, `src/Lantern.Till/Services/TillSessionService.cs`, `src/Lantern.Till/Services/TillSetup.cs`
- Test: `tests/Lantern.IntegrationTests/TillSaleTests.cs`

**Interfaces:**
- Consumes: `TillSessionService` internals (`WithDeviceAsync`, `EnsureOutletAsync`, `ExpireIdleCashierAsync`, `EndCashierAsync`) from Task 4; the API's `POST /outlets/{outletId}/sales` (Plan 2).
- Produces:
  - Models:
    - `SaleItem(string Sku, int Quantity)`
    - `ReceiptLine(string Sku, string Name, int Quantity, decimal UnitPrice, decimal Amount)`
    - `Receipt(Guid SaleId, string OutletId, string TillAccount, string CashierCode, string CashierName, string ServedBy, IReadOnlyList<ReceiptLine> Lines, decimal Total, DateTimeOffset At)`
    - `SaleOutcome { Ok, Locked, TillSignedOut, Rejected }`
    - `SaleResult(SaleOutcome Outcome, Receipt? Receipt, string Message)`
  - `LanternApiClient.RingSaleAsync(outletId, cashierAccessToken, items, ct)` → `(Receipt? Receipt, int Status)`.
  - `TillSessionService.RingSaleAsync(string deviceId, IReadOnlyList<SaleItem> items, ct)` → `SaleResult`. It always force-refreshes the outlet login first, and refreshes the cashier token if it's within 30 s of expiry.

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/TillSaleTests.cs`:
```csharp
using Lantern.IntegrationTests.Infrastructure;
using Lantern.Till.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class TillSaleTests(KeycloakFixture kc) : IAsyncLifetime
{
    private static readonly SaleItem[] TwoCoffees = [new("SKU-100", 2)];
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "lantern-till-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private TillFactory _till = null!;

    public Task InitializeAsync()
    {
        _till = new TillFactory(kc, _dataDir, s => s.AddSingleton<TimeProvider>(_time));
        return Task.CompletedTask;
    }

    private async Task<(string DeviceId, string AccountId, string Account)> RegisterWithCashierAsync(string code = "c-1001", string pin = "1111")
    {
        var (accountId, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        using var browser = new TillBrowser(_till);
        await browser.RegisterAsync(account);
        Assert.True((await _till.Session.SignInCashierAsync(browser.DeviceId!, new PinAttempt(code, pin, null))).Succeeded);
        return (browser.DeviceId!, accountId, account);
    }

    [Fact]
    public async Task Sale_receipt_names_the_cashier_and_this_till()
    {
        var (device, _, account) = await RegisterWithCashierAsync();

        var result = await _till.Session.RingSaleAsync(device, TwoCoffees);

        Assert.Equal(SaleOutcome.Ok, result.Outcome);
        Assert.Equal("Siti Aminah (c-1001)", result.Receipt!.ServedBy);
        Assert.Equal(account, result.Receipt.TillAccount);
        Assert.Equal(90.00m, result.Receipt.Total);
    }

    [Fact]
    public async Task Released_till_drops_the_cashier_before_the_next_sale()
    {
        var (device, accountId, _) = await RegisterWithCashierAsync("c-1002", "2222");

        await kc.RevokeTillLoginAsync(accountId); // the outlet access token is still unexpired

        var result = await _till.Session.RingSaleAsync(device, TwoCoffees);
        Assert.Equal(SaleOutcome.TillSignedOut, result.Outcome);
        Assert.Equal(TillMode.NotRegistered, (await _till.Session.GetStatusAsync(device)).Mode);
    }

    [Fact]
    public async Task Sale_after_the_idle_window_is_refused_even_without_a_poll()
    {
        var (device, _, _) = await RegisterWithCashierAsync();

        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var result = await _till.Session.RingSaleAsync(device, TwoCoffees);

        Assert.Equal(SaleOutcome.Locked, result.Outcome);
    }

    [Fact]
    public async Task Sale_on_a_locked_till_is_refused()
    {
        var (device, _, _) = await RegisterWithCashierAsync();
        await _till.Session.LockAsync(device);

        Assert.Equal(SaleOutcome.Locked, (await _till.Session.RingSaleAsync(device, TwoCoffees)).Outcome);
    }

    [Fact]
    public async Task Sales_keep_working_past_the_cashier_access_token_lifetime()
    {
        var (device, _, _) = await RegisterWithCashierAsync();

        _time.Advance(TimeSpan.FromMinutes(6)); // cashier access token (5 min) expired, but within the idle window
        var result = await _till.Session.RingSaleAsync(device, TwoCoffees);

        Assert.Equal(SaleOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task Invalid_sale_is_rejected_with_a_message()
    {
        var (device, _, _) = await RegisterWithCashierAsync();

        var result = await _till.Session.RingSaleAsync(device, [new SaleItem("SKU-999", 1)]);

        Assert.Equal(SaleOutcome.Rejected, result.Outcome);
        Assert.Equal("That sale couldn't be recorded. Check the items and try again.", result.Message);
    }

    public async Task DisposeAsync()
    {
        await _till.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~TillSaleTests`
Expected: FAIL, with compilation errors: `SaleItem`, `SaleOutcome` and `RingSaleAsync` are not found.

- [ ] **Step 3: Implement**

`src/Lantern.Till/Services/TillModels.cs`: append:
```csharp
public sealed record SaleItem(string Sku, int Quantity);

public sealed record ReceiptLine(string Sku, string Name, int Quantity, decimal UnitPrice, decimal Amount);

public sealed record Receipt(Guid SaleId, string OutletId, string TillAccount, string CashierCode, string CashierName,
    string ServedBy, IReadOnlyList<ReceiptLine> Lines, decimal Total, DateTimeOffset At);

public enum SaleOutcome { Ok, Locked, TillSignedOut, Rejected }

public sealed record SaleResult(SaleOutcome Outcome, Receipt? Receipt, string Message);
```

`src/Lantern.Till/Services/LanternApiClient.cs`:
```csharp
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Lantern.Till.Services;

public sealed class LanternApiClient(IHttpClientFactory httpFactory, IOptions<TillOptions> options)
{
    public async Task<(Receipt? Receipt, int Status)> RingSaleAsync(string outletId, string cashierAccessToken,
        IReadOnlyList<SaleItem> items, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.ApiBaseUrl.TrimEnd('/')}/outlets/{outletId}/sales")
        {
            Content = JsonContent.Create(new { items = items.Select(i => new { sku = i.Sku, quantity = i.Quantity }) })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierAccessToken);
        try
        {
            using var response = await httpFactory.CreateClient("lantern-api").SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? (await response.Content.ReadFromJsonAsync<Receipt>(ct), (int)response.StatusCode)
                : (null, (int)response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, 503);
        }
    }
}
```

`src/Lantern.Till/Services/TillSessionService.cs`:
- add `LanternApiClient api` to the primary constructor, after `KeycloakTillClient keycloak`
- add this method after `SignInCashierAsync`:
```csharp
    /// <summary>
    /// Spec §6.2 step 6 and §6.5: re-check the outlet login before every sale, refuse when idle or locked,
    /// and send the sale under the cashier's token.
    /// </summary>
    public async Task<SaleResult> RingSaleAsync(string deviceId, IReadOnlyList<SaleItem> items, CancellationToken ct = default) =>
        await WithDeviceAsync(deviceId, async (state, registration) =>
        {
            var outlet = await EnsureOutletAsync(registration, state, force: true, ct);
            if (outlet == OutletCheck.SignedOut)
                return new SaleResult(SaleOutcome.TillSignedOut, null, TillMessages.For("outlet_session_invalid"));
            if (outlet == OutletCheck.Unavailable)
                return new SaleResult(SaleOutcome.Rejected, null, TillMessages.For(TillMessages.Unavailable));

            await ExpireIdleCashierAsync(state, ct);
            if (state.Cashier is not { } cashier || !await EnsureCashierTokenAsync(state, cashier, ct))
                return new SaleResult(SaleOutcome.Locked, null, "Sign in with your PIN to continue.");

            var (receipt, status) = await api.RingSaleAsync(registration.OutletId, cashier.AccessToken, items, ct);
            if (receipt is null)
                return new SaleResult(SaleOutcome.Rejected, null, status switch
                {
                    400 => "That sale couldn't be recorded. Check the items and try again.",
                    401 or 403 => "You don't have permission to ring sales here.",
                    _ => "The sale couldn't be sent. Try again."
                });

            cashier.LastActivity = time.GetUtcNow();
            return new SaleResult(SaleOutcome.Ok, receipt, "");
        }, new SaleResult(SaleOutcome.TillSignedOut, null, TillMessages.For("outlet_session_invalid")), ct);

    private async Task<bool> EnsureCashierTokenAsync(DeviceState state, CashierSession cashier, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (cashier.AccessExpiresAt - now > TimeSpan.FromSeconds(30)) return true;

        var (tokens, _) = await keycloak.RefreshAsync(cashier.RefreshToken, ct);
        if (tokens is null)
        {
            await EndCashierAsync(state, ct);
            return false;
        }
        cashier.AccessToken = tokens.AccessToken;
        cashier.RefreshToken = tokens.RefreshToken ?? cashier.RefreshToken;
        cashier.AccessExpiresAt = now.AddSeconds(tokens.ExpiresIn);
        return true;
    }
```

`src/Lantern.Till/Services/TillSetup.cs`: after `services.AddSingleton<KeycloakTillClient>();` add:
```csharp
        services.AddSingleton<LanternApiClient>();
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~TillSaleTests`
Expected: PASS (6 tests). If `Sales_keep_working_past_the_cashier_access_token_lifetime` fails with 401 from the API, the cashier refresh isn't happening. Check that the cashier's `RefreshToken` was captured at sign-in: the PIN grant returns one because the `till` client uses refresh tokens.

Then run: `dotnet test`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Lantern.Till tests
git commit -m "feat: ring sales from the till with an outlet re-check before each sale"
```

---

### Task 6: The till screens (sign-in prompt, PIN pad, sale and receipt)

**Files:**
- Create: `src/Lantern.Till/Components/Till/TillScreen.razor`, `PinPad.razor`, `SaleScreen.razor`, `TillCatalog.cs`
- Modify: `src/Lantern.Till/Components/Pages/Home.razor`, `src/Lantern.Till/Components/_Imports.razor`, `src/Lantern.Till/wwwroot/app.css`
- Test: `tests/Lantern.IntegrationTests/TillPageTests.cs`

**Interfaces:**
- Consumes: `TillSessionService`, `TillStatus`, `PinAttempt`, `PinResult`, `SaleItem`, `SaleResult`, `Receipt` (Tasks 4–5), and `DeviceCookie` (Task 3).
- Produces: markup with stable `data-testid` hooks for Plan 5's Playwright tests:
  - `not-registered`, `register-link`
  - `till-account`, `outlet-id`, `cashier`, `lock`
  - `pin-pad`, `cashier-code`, `pin`, `new-pin`, `pin-submit`, `pin-message`
  - `sale`, `add-SKU-100`, `add-SKU-200`, `cart`, `charge`, `sale-message`
  - `receipt`, `sign-out-till`, `keycloak-unavailable`

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/TillPageTests.cs`:
```csharp
using Lantern.IntegrationTests.Infrastructure;
using Lantern.Till.Services;
using Microsoft.Data.Sqlite;

namespace Lantern.IntegrationTests;

/// <summary>The prerendered till page shows the right screen for each till state.</summary>
[Collection(KeycloakCollection.Name)]
public sealed class TillPageTests(KeycloakFixture kc) : IAsyncLifetime
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "lantern-till-" + Guid.NewGuid().ToString("N"));
    private TillFactory _till = null!;

    public Task InitializeAsync()
    {
        _till = new TillFactory(kc, _dataDir);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Unregistered_browser_sees_the_sign_in_prompt()
    {
        using var browser = new TillBrowser(_till);

        var page = await browser.GetAsync("/");

        Assert.Contains("data-testid=\"not-registered\"", page.Html);
        Assert.Contains("href=\"/register\"", page.Html);
    }

    [Fact]
    public async Task Registered_till_shows_its_account_and_the_pin_pad()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        using var browser = new TillBrowser(_till);

        var page = await browser.RegisterAsync(account);

        Assert.Contains("data-testid=\"pin-pad\"", page.Html);
        Assert.Contains(account, page.Html);
        Assert.DoesNotContain("data-testid=\"sale\"", page.Html);
    }

    [Fact]
    public async Task Signed_in_cashier_sees_the_sale_screen_with_their_name()
    {
        var (_, account) = await kc.CreateTempTillAsync("/Outlets/Bangsar");
        using var browser = new TillBrowser(_till);
        await browser.RegisterAsync(account);
        await _till.Session.SignInCashierAsync(browser.DeviceId!, new PinAttempt("c-1001", "1111", null));

        var page = await browser.GetAsync("/");

        Assert.Contains("data-testid=\"sale\"", page.Html);
        Assert.Contains("Siti Aminah (c-1001)", page.Html);
        Assert.Contains("data-testid=\"sign-out-till\"", page.Html);
    }

    public async Task DisposeAsync()
    {
        await _till.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~TillPageTests`
Expected: FAIL. The template's `Home.razor` renders "Hello, world!", so none of the `data-testid` markers are present.

- [ ] **Step 3: Implement the screens**

`src/Lantern.Till/Components/_Imports.razor`: append:
```razor
@using Lantern.Till.Services
@using Lantern.Till.Components.Till
```

`src/Lantern.Till/Components/Pages/Home.razor` (replace):
```razor
@page "/"

<PageTitle>Lantern Till</PageTitle>

<TillScreen DeviceId="@deviceId" @rendermode="InteractiveServer" />

@code {
    // This page renders statically, so the request (and its device cookie) is available here; the id is
    // passed to the interactive screen as a parameter, which Blazor protects in the page markup.
    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    private string? deviceId;

    protected override void OnInitialized() =>
        deviceId = HttpContext is null ? null : DeviceCookie.Read(HttpContext.Request);
}
```

`src/Lantern.Till/Components/Till/TillCatalog.cs`:
```csharp
namespace Lantern.Till.Components.Till;

/// <summary>The till's product buttons. Prices come back from the API on the receipt.</summary>
public static class TillCatalog
{
    public sealed record Item(string Sku, string Name);

    public static readonly IReadOnlyList<Item> Items =
    [
        new("SKU-100", "House Blend 1kg"),
        new("SKU-200", "Oat Milk 1L")
    ];

    public static string NameOf(string sku) => Items.FirstOrDefault(i => i.Sku == sku)?.Name ?? sku;
}
```

`src/Lantern.Till/Components/Till/TillScreen.razor`:
```razor
@implements IAsyncDisposable
@inject TillSessionService Till

<main class="till" data-testid="till">
    @if (status is null)
    {
        <p>Loading…</p>
    }
    else if (status.Mode == TillMode.NotRegistered)
    {
        <section class="card" data-testid="not-registered">
            <h1>This till isn't signed in</h1>
            <p>Sign in with this till's outlet account. You only need to do this once.</p>
            <a class="primary" href="/register" data-enhance-nav="false" data-testid="register-link">Sign in this till</a>
        </section>
    }
    else
    {
        <header class="till-bar">
            <span data-testid="till-account">@status.TillAccount</span>
            <span class="muted" data-testid="outlet-id">@status.OutletId</span>
            @if (status.Cashier is { } cashier)
            {
                <span class="cashier" data-testid="cashier">@cashier.Name (@cashier.Code)</span>
                <button @onclick="LockAsync" data-testid="lock">Lock</button>
            }
        </header>

        @if (status.KeycloakUnavailable)
        {
            <p class="warning" role="status" data-testid="keycloak-unavailable">Sign-in is temporarily unavailable.</p>
        }

        @if (status.Mode == TillMode.Locked)
        {
            <PinPad OnSignIn="SignInAsync" />
        }
        else
        {
            <SaleScreen OnCharge="ChargeAsync" />
        }

        <footer class="till-footer">
            <button class="link" @onclick="SignOutTillAsync" data-testid="sign-out-till">Sign out this till</button>
        </footer>
    }
</main>

@code {
    [Parameter] public string? DeviceId { get; set; }

    private TillStatus? status;
    private readonly CancellationTokenSource stop = new();

    protected override async Task OnInitializedAsync() => status = await Till.GetStatusAsync(DeviceId, stop.Token);

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && RendererInfo.IsInteractive) _ = PollAsync();
    }

    /// <summary>Picks up the idle lock and a released till without waiting for the next tap.</summary>
    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                status = await Till.GetStatusAsync(DeviceId, stop.Token);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<PinResult> SignInAsync(PinAttempt attempt)
    {
        var result = await Till.SignInCashierAsync(DeviceId!, attempt, stop.Token);
        status = await Till.GetStatusAsync(DeviceId, stop.Token);
        return result;
    }

    private async Task<SaleResult> ChargeAsync(IReadOnlyList<SaleItem> items)
    {
        var result = await Till.RingSaleAsync(DeviceId!, items, stop.Token);
        if (result.Outcome != SaleOutcome.Ok) status = await Till.GetStatusAsync(DeviceId, stop.Token);
        return result;
    }

    private async Task LockAsync()
    {
        await Till.LockAsync(DeviceId!, stop.Token);
        status = await Till.GetStatusAsync(DeviceId, stop.Token);
    }

    private async Task SignOutTillAsync()
    {
        await Till.SignOutTillAsync(DeviceId!, stop.Token);
        status = await Till.GetStatusAsync(DeviceId, stop.Token);
    }

    public ValueTask DisposeAsync()
    {
        stop.Cancel();
        stop.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

`src/Lantern.Till/Components/Till/PinPad.razor`:
```razor
<section class="card pin-pad" data-testid="pin-pad">
    <h1>@(changing ? "Choose a new PIN" : "Cashier sign-in")</h1>

    <label>Cashier code
        <input data-testid="cashier-code" @bind="code" disabled="@changing" autocomplete="off" />
    </label>
    <label>@(changing ? "Temporary PIN" : "PIN")
        <input data-testid="pin" type="password" inputmode="numeric" @bind="pin" disabled="@changing" autocomplete="off" />
    </label>
    @if (changing)
    {
        <label>New PIN
            <input data-testid="new-pin" type="password" inputmode="numeric" @bind="newPin" autocomplete="off" />
        </label>
    }

    <button class="primary" data-testid="pin-submit" disabled="@busy" @onclick="SubmitAsync">
        @(changing ? "Save PIN and sign in" : "Sign in")
    </button>

    @if (!string.IsNullOrEmpty(message))
    {
        <p class="error" role="alert" data-testid="pin-message">@message</p>
    }
</section>

@code {
    [Parameter, EditorRequired] public Func<PinAttempt, Task<PinResult>> OnSignIn { get; set; } = default!;

    private string code = "";
    private string pin = "";
    private string newPin = "";
    private bool changing;
    private bool busy;
    private string? message;

    private async Task SubmitAsync()
    {
        busy = true;
        try
        {
            var result = await OnSignIn(new PinAttempt(code, pin, changing ? newPin : null));
            if (result.Succeeded)
            {
                (pin, newPin, changing, message) = ("", "", false, null);
                return;
            }

            // Stay on the new-PIN screen while the temporary PIN is right but the new one breaks a rule.
            changing = result.ChangePinRequired || (changing && result.ErrorCode?.StartsWith("pin_rule", StringComparison.Ordinal) == true);
            message = result.Message;
            newPin = "";
            if (!changing) pin = "";
        }
        finally
        {
            busy = false;
        }
    }
}
```

`src/Lantern.Till/Components/Till/SaleScreen.razor`:
```razor
<section class="card sale" data-testid="sale">
    <div class="catalog">
        @foreach (var item in TillCatalog.Items)
        {
            <button data-testid="add-@item.Sku" @onclick="() => Add(item.Sku)">@item.Name</button>
        }
    </div>

    <ul class="cart" data-testid="cart">
        @foreach (var (sku, quantity) in cart)
        {
            <li>@quantity × @TillCatalog.NameOf(sku)</li>
        }
    </ul>

    <button class="primary" data-testid="charge" disabled="@(cart.Count == 0 || busy)" @onclick="ChargeAsync">Charge</button>

    @if (!string.IsNullOrEmpty(message))
    {
        <p class="error" role="alert" data-testid="sale-message">@message</p>
    }

    @if (receipt is not null)
    {
        <article class="receipt" data-testid="receipt">
            <p>Served by: <strong>@receipt.ServedBy</strong></p>
            <p>Till: @receipt.TillAccount · @receipt.OutletId</p>
            <ul>
                @foreach (var line in receipt.Lines)
                {
                    <li>@line.Quantity × @line.Name <span>@line.Amount.ToString("0.00")</span></li>
                }
            </ul>
            <p class="total">Total <strong>@receipt.Total.ToString("0.00")</strong></p>
        </article>
    }
</section>

@code {
    [Parameter, EditorRequired] public Func<IReadOnlyList<SaleItem>, Task<SaleResult>> OnCharge { get; set; } = default!;

    private readonly Dictionary<string, int> cart = new();
    private Receipt? receipt;
    private string? message;
    private bool busy;

    private void Add(string sku)
    {
        cart[sku] = cart.GetValueOrDefault(sku) + 1;
        (receipt, message) = (null, null);
    }

    private async Task ChargeAsync()
    {
        busy = true;
        try
        {
            var result = await OnCharge(cart.Select(c => new SaleItem(c.Key, c.Value)).ToList());
            if (result.Outcome == SaleOutcome.Ok)
            {
                receipt = result.Receipt;
                cart.Clear();
            }
            else
            {
                message = result.Message;
            }
        }
        finally
        {
            busy = false;
        }
    }
}
```

`src/Lantern.Till/wwwroot/app.css` (replace):
```css
:root {
    --bg: #f6f4ef;
    --card: #ffffff;
    --ink: #1f1d1a;
    --muted: #6b665e;
    --accent: #b45309;
    --danger: #b91c1c;
    --line: #e4e0d8;
}

* { box-sizing: border-box; }

body {
    margin: 0;
    font: 18px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif;
    background: var(--bg);
    color: var(--ink);
}

.till { max-width: 40rem; margin: 0 auto; padding: 1rem; }
.card { background: var(--card); border: 1px solid var(--line); border-radius: 12px; padding: 1.5rem; margin: 1rem 0; }
.till-bar { display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; }
.till-bar .cashier { margin-left: auto; font-weight: 600; }
.muted { color: var(--muted); }

label { display: block; margin: 0.75rem 0; }
input { display: block; width: 100%; font-size: 1.5rem; padding: 0.6rem; border: 1px solid var(--line); border-radius: 8px; }

button, .primary { font: inherit; min-height: 3rem; padding: 0.5rem 1.25rem; border-radius: 8px; border: 1px solid var(--line); background: var(--card); cursor: pointer; }
.primary { display: inline-block; background: var(--accent); border-color: var(--accent); color: #fff; text-decoration: none; }
button:disabled { opacity: 0.5; cursor: not-allowed; }
.link { border: none; background: none; color: var(--muted); text-decoration: underline; }

.catalog { display: grid; grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr)); gap: 0.75rem; }
.catalog button { min-height: 5rem; }
.cart { padding-left: 1.25rem; }
.receipt { border-top: 2px dashed var(--line); margin-top: 1rem; padding-top: 1rem; }
.receipt li span { float: right; }
.total { font-size: 1.25rem; }

.error { color: var(--danger); font-weight: 600; }
.warning { background: #fef3c7; padding: 0.75rem 1rem; border-radius: 8px; }
.till-footer { text-align: center; margin-top: 2rem; }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~TillPageTests`
Expected: PASS (3 tests).
Then run: `dotnet test`
Expected: all pass.

- [ ] **Step 5: Try it by hand**

```bash
docker compose up -d --wait
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH
dotnet run --project src/Lantern.Till
```
Then, in a browser at http://localhost:5400:
1. Click "Sign in this till" and sign in as `outlet-bangsar-2`. You land back on the PIN pad.
2. Enter `c-1001` and PIN `1111`, add two coffees, and charge. The receipt reads "Served by: Siti Aminah (c-1001)".
3. Click "Sign out this till".

Stop the app with Ctrl+C. This is a manual check only; Plan 5 automates it.

- [ ] **Step 6: Commit**

```bash
git add src/Lantern.Till tests
git commit -m "feat: till screens for sign-in prompt, pin pad, sale and receipt"
```

---

### Task 7: The till in Docker Compose, smoke check and README

**Files:**
- Create: `src/Lantern.Till/Dockerfile`
- Modify: `docker-compose.yml`, `scripts/smoke.sh`, `README.md`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - Compose service `till` on `127.0.0.1:5400`, with volume `till-data` at `/data`.
  - `scripts/smoke.sh` also checks that the till answers with its sign-in prompt and is bound to localhost only.

- [ ] **Step 1: Write the failing smoke check**

`scripts/smoke.sh`:
- change the loop line to include the till:
```bash
for svc_port in keycloak:8080 mailpit:8025 api:8080 till:8080; do
```
- add before `echo "OK: stack healthy"`:
```bash
till_page=$(curl -fsS http://localhost:5400/) || fail "Till not reachable on localhost:5400"
grep -q 'data-testid="not-registered"' <<<"$till_page" || fail "Till did not show its sign-in prompt"
```

Run: `./scripts/smoke.sh`
Expected: FAIL. `docker compose port till 8080` errors because there's no `till` service yet.

- [ ] **Step 2: Containerise the till**

`src/Lantern.Till/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Lantern.Till/Lantern.Till.csproj src/Lantern.Till/
RUN dotnet restore src/Lantern.Till/Lantern.Till.csproj
COPY src/Lantern.Till/ src/Lantern.Till/
RUN dotnet publish src/Lantern.Till/Lantern.Till.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The registration database and Data Protection keys live on a volume the non-root app user owns.
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID
ENTRYPOINT ["dotnet", "Lantern.Till.dll"]
```

`docker-compose.yml`: add this service after `api`:
```yaml
  till:
    build:
      context: .
      dockerfile: src/Lantern.Till/Dockerfile
    image: lantern-till:dev
    environment:
      Till__KeycloakBaseUrl: http://keycloak:8080
      Till__Issuer: http://localhost:8080/realms/lantern
      Till__ApiBaseUrl: http://api:8080
      Till__DataDirectory: /data
    volumes:
      - till-data:/data
    ports:
      - "127.0.0.1:5400:8080"
    depends_on:
      keycloak:
        condition: service_healthy
      api:
        condition: service_started
```
and add `till-data:` under the top-level `volumes:` next to `postgres-data:`.

- [ ] **Step 3: Run the smoke check and confirm it passes**

```bash
docker compose up -d --build --wait
./scripts/smoke.sh
```
Expected: `OK: issuer is …`, then `OK: stack healthy`.

Then check by hand that the registration survives a container restart:
1. In a browser, sign in this till at http://localhost:5400 as `outlet-bangsar-2`.
2. Run `docker compose restart till`.
3. Reload the page. It should still show the PIN pad, not the sign-in prompt.

Afterwards, release the account with "Sign out this till".

- [ ] **Step 4: Update the README**

`README.md`:
- replace the status line `> Work in progress. Plan 1 of 4 …` with:
```markdown
> Work in progress. Plans 1–3 of 5 (foundation, till backend, till app) are done. See
> `docs/superpowers/plans/2026-10-01-roadmap.md`.
```
- add a row to the services table:
```markdown
| Till (sign in as `outlet-bangsar-2`, then PIN `c-1001` / `1111`) | http://localhost:5400 |
```
- add a section before `## Tests`:
````markdown
## Try the till

1. Open http://localhost:5400 and click **Sign in this till**. Use `outlet-bangsar-2` / `Lantern!2026`.
   The till stays signed in from now on, even across restarts.
2. On the PIN pad, enter cashier `c-1001` and PIN `1111`. Ring a sale: the receipt names Siti and the till.
3. Try a second browser with the same till account: Keycloak refuses it, because one account means one till.
4. **Sign out this till** frees the account. An outlet manager can also release it with
   `DELETE /outlets/BGS/tills/{id}/session` on the API.

Cashier `c-3001` (PJ) has a temporary PIN `5555` and is asked to choose a new one.
````

- [ ] **Step 5: Commit**

```bash
git add src/Lantern.Till/Dockerfile docker-compose.yml scripts/smoke.sh README.md
git commit -m "feat: run the till in compose with smoke check and readme"
```
