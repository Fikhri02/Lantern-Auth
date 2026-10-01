# Lantern Auth — Plan 1: Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reproducible Keycloak realm plus a .NET 10 API that enforces every HQ and outlet-manager role rule, rejects deactivated users, and manages HQ staff through Keycloak. All of it is proven by integration tests against a real Keycloak.

**Architecture:** Keycloak 26.7.5 runs from a custom image that already contains our Java extension jar (only the `outlet_id` token mapper for now). The `lantern` realm is imported from one JSON file. `Lantern.Api` validates JWTs locally, double-checks each token with Keycloak introspection (short cache), and applies named authorization policies. Staff management calls the Keycloak Admin API with a dedicated service account. Integration tests start Keycloak and Mailpit with Testcontainers, importing the same realm file plus a test-only password-grant client.

**Tech Stack:** .NET 10 (ASP.NET Core minimal APIs), Keycloak 26.7.5, Java 21 + Maven (inside Docker), PostgreSQL 17, Mailpit, xUnit 2.9, Testcontainers for .NET 4.15, JUnit 5 + Mockito.

**Spec:** `docs/superpowers/specs/2026-10-01-lantern-auth-design.md`. The roadmap is in `docs/superpowers/plans/2026-10-01-roadmap.md`.

## Global Constraints

- Target framework `net10.0` everywhere. `global.json` pins SDK `10.0.100` with `rollForward: latestFeature`.
- Keycloak image `quay.io/keycloak/keycloak:26.7.5`. The plugin compiles against Keycloak artifacts `26.7.5` with Java release `21`.
- Package versions: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, `Testcontainers.Keycloak` 4.15.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.10.1, JUnit Jupiter 5.12.2, Mockito 5.24.0.
- Fictional setting only: "Lantern Mart". No employer names, code, schema or endpoints, anywhere.
- Demo password for every seeded user: `Lantern!2026`. Client secrets in the repo are dev-only values ending in `-dev-secret`.
- Ports: Keycloak 8080, API 5100, Mailpit 8025.
- Token issuer as seen by everyone: `http://localhost:8080/realms/lantern`. Containers reach Keycloak at `http://keycloak:8080`.
- Commits: conventional style (`feat:`, `test:`, `docs:`, `chore:`), **no trailers of any kind**. Work on branch `irfan/foundation`.
- Docker must be running for Keycloak image builds and for every test except Task 1's and the introspector unit tests.

**Spec deviations, decided here:**
1. The Keycloak Dockerfile does not run `kc.sh build`. `start` auto-builds at boot, and tests use `start-dev`. One image serves both, at the cost of a slower first boot.
2. The spec's `OutletScoped` policy becomes one requirement type used by two policies: `OutletManage` (this plan) and `OutletSell` (Plan 2).
3. The introspection cache setting lives at `Keycloak:IntrospectionCacheSeconds`.
4. Tests add a `test-runner` client with password grant **only to the realm copy imported by the tests**. The committed realm has no generic password-grant client.

## Review Focus

1. **An admin deactivates their own account.** Expected: 400 and the account stays enabled, so the last admin can't lock everyone out. Test in Task 9.
2. **Keycloak is unreachable or errors during introspection.** Expected: the API answers 401, never 500, and never lets the token through. Tests in Task 8.
3. **A staff member's departments are set to an empty list.** Expected: 400 and memberships unchanged, rather than silently removing all HQ access. Test in Task 9.
4. **The same purchase order is approved twice.** Expected: the second attempt gets 409 and the first approver is kept. Test in Task 6.
5. **The outlet ID in the URL is in a different case** (`/outlets/bgs/stock` for a Bangsar manager). Expected: treated as `BGS`. Test in Task 7.

---

## File Structure

```
lantern-auth/
  .gitignore  .dockerignore  global.json  Directory.Build.props  Lantern.slnx
  docker-compose.yml
  README.md
  scripts/verify-hostname.sh          # Task 3: hostname spike as a repeatable check
  scripts/smoke.sh                    # Task 10
  keycloak/
    .dockerignore
    Dockerfile                        # Maven build stage → Keycloak 26.7.5 with our jar
    realm/lantern-realm.json          # roles, groups, clients, users, SMTP, lifetimes
    pin-authenticator/                # Maven project (name kept from spec; holds all our extensions)
      pom.xml
      src/main/java/dev/lantern/keycloak/OutletIdMapper.java
      src/main/resources/META-INF/services/org.keycloak.protocol.ProtocolMapper
      src/test/java/dev/lantern/keycloak/OutletIdMapperTest.java
  src/Lantern.Api/
    Lantern.Api.csproj  Dockerfile  appsettings.json  Program.cs
    Auth/KeycloakOptions.cs           # config shape + derived URLs
    Auth/AuthSetup.cs                 # JWT bearer + introspection hook
    Auth/Roles.cs                     # role name constants
    Auth/ClaimsPrincipalExtensions.cs # GetSub, GetDisplayName
    Auth/Policies.cs                  # policy names + registration
    Auth/ForbiddenResultHandler.cs    # 403 body names the policy
    Auth/NotCreatorRequirement.cs     # separation of duties for PO approval
    Auth/OutletRequirement.cs         # role + route outlet must match token outlet_id
    Auth/TokenIntrospector.cs         # Keycloak introspection with cache
    Data/DemoStore.cs                 # in-memory seeded business data
    Endpoints/MeEndpoints.cs
    Endpoints/HqEndpoints.cs
    Endpoints/PurchaseOrderEndpoints.cs
    Endpoints/OutletEndpoints.cs
    Endpoints/StaffEndpoints.cs
    Keycloak/KeycloakModels.cs        # Admin API DTOs
    Keycloak/Departments.cs           # department ↔ group path
    Keycloak/KeycloakAdminClient.cs   # service-account Admin API calls
  tests/Lantern.IntegrationTests/
    Lantern.IntegrationTests.csproj
    Infrastructure/KeycloakFixture.cs # Keycloak + Mailpit containers, tokens, admin helpers
    Infrastructure/ApiFactory.cs      # WebApplicationFactory pointed at the containers
    Infrastructure/KeycloakCollection.cs
    HealthTests.cs  AuthenticationTests.cs  RolePolicyTests.cs  PurchaseOrderApprovalTests.cs
    OutletScopeTests.cs  TokenIntrospectorTests.cs  DeactivationTests.cs  StaffManagementTests.cs
```

---

### Task 1: Repository skeleton and API health endpoint

**Files:**
- Create: `.gitignore`, `global.json`, `Directory.Build.props`, `Lantern.slnx` (via CLI; older SDK templates may produce `Lantern.sln`)
- Create: `src/Lantern.Api/Lantern.Api.csproj`, `src/Lantern.Api/Program.cs`, `src/Lantern.Api/appsettings.json`
- Create: `tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`
- Test: `tests/Lantern.IntegrationTests/HealthTests.cs`

**Interfaces:**
- Produces: `public partial class Program` in `Lantern.Api`, so `WebApplicationFactory<Program>` works. `GET /health` returns `{"status":"ok"}`.

- [ ] **Step 1: Install the .NET 10 SDK and create the branch**

```bash
dotnet --list-sdks | grep -q '^10\.' || brew install --cask dotnet-sdk
dotnet --list-sdks | grep '^10\.'
cd ~/projects/Personal/DotNet/lantern-auth
git checkout -b irfan/foundation
```
Expected: at least one `10.0.x` SDK listed, and the branch switched.

- [ ] **Step 2: Create root files**

`.gitignore`:
```gitignore
bin/
obj/
target/
.vs/
.idea/
*.user
TestResults/
```

`global.json`:
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create the API project**

`src/Lantern.Api/Lantern.Api.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.12" />
  </ItemGroup>
</Project>
```

`src/Lantern.Api/appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Keycloak": {
    "BaseUrl": "http://localhost:8080",
    "Realm": "lantern",
    "Issuer": "http://localhost:8080/realms/lantern",
    "Audience": "lantern-api",
    "ApiClientId": "lantern-api",
    "ApiClientSecret": "lantern-api-dev-secret",
    "AdminClientId": "api-admin-svc",
    "AdminClientSecret": "api-admin-svc-dev-secret",
    "IntrospectionCacheSeconds": 15
  }
}
```

- [ ] **Step 4: Create the test project and write the failing test**

`tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageReference Include="Testcontainers.Keycloak" Version="4.15.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Lantern.Api\Lantern.Api.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

`tests/Lantern.IntegrationTests/HealthTests.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lantern.IntegrationTests;

public sealed class HealthTests
{
    [Fact]
    public async Task Health_returns_ok_without_authentication()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>("/health");

        Assert.Equal("ok", body.GetProperty("status").GetString());
    }
}
```

`src/Lantern.Api/Program.cs` (placeholder so the solution compiles; no `/health` yet):
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

public partial class Program;
```

```bash
dotnet new sln -n Lantern
dotnet sln add src/Lantern.Api/Lantern.Api.csproj tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj
```

- [ ] **Step 5: Run the test and confirm it fails**

Run: `dotnet test --filter FullyQualifiedName~HealthTests`
Expected: FAIL. The response is 404, so `GetFromJsonAsync` throws `HttpRequestException`.

- [ ] **Step 6: Implement `/health`**

`src/Lantern.Api/Program.cs`:
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
```

- [ ] **Step 7: Run the test and confirm it passes**

Run: `dotnet test --filter FullyQualifiedName~HealthTests`
Expected: PASS (1 test).

- [ ] **Step 8: Commit**

```bash
git add .gitignore global.json Directory.Build.props Lantern.sln* src tests
git commit -m "chore: scaffold solution with API health endpoint"
```

---

### Task 2: Keycloak extension jar with the `outlet_id` mapper, and the Keycloak image

**Files:**
- Create: `keycloak/pin-authenticator/pom.xml`
- Create: `keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/OutletIdMapper.java`
- Create: `keycloak/pin-authenticator/src/main/resources/META-INF/services/org.keycloak.protocol.ProtocolMapper`
- Create: `keycloak/Dockerfile`, `keycloak/.dockerignore`
- Test: `keycloak/pin-authenticator/src/test/java/dev/lantern/keycloak/OutletIdMapperTest.java`

**Interfaces:**
- Produces: protocol mapper provider id `lantern-outlet-id-mapper`. Config keys: `claim.name`, `access.token.claim`, `id.token.claim`, `userinfo.token.claim`. It emits the first non-blank `outlet_id` attribute found on the user's groups.
- Produces: Docker build context `keycloak/` → image with `/opt/keycloak/providers/lantern-keycloak-extensions.jar`. Task 3 tags it `lantern-keycloak:dev`; Task 4 builds it from tests as `lantern-keycloak-test:26.7.5`.

- [ ] **Step 1: Create the Maven project**

`keycloak/pin-authenticator/pom.xml`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<project xmlns="http://maven.apache.org/POM/4.0.0"
         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 https://maven.apache.org/xsd/maven-4.0.0.xsd">
  <modelVersion>4.0.0</modelVersion>
  <groupId>dev.lantern</groupId>
  <artifactId>lantern-keycloak-extensions</artifactId>
  <version>0.1.0</version>
  <packaging>jar</packaging>

  <properties>
    <maven.compiler.release>21</maven.compiler.release>
    <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
    <keycloak.version>26.7.5</keycloak.version>
    <junit.version>5.12.2</junit.version>
    <mockito.version>5.24.0</mockito.version>
  </properties>

  <dependencies>
    <dependency>
      <groupId>org.keycloak</groupId>
      <artifactId>keycloak-core</artifactId>
      <version>${keycloak.version}</version>
      <scope>provided</scope>
    </dependency>
    <dependency>
      <groupId>org.keycloak</groupId>
      <artifactId>keycloak-server-spi</artifactId>
      <version>${keycloak.version}</version>
      <scope>provided</scope>
    </dependency>
    <dependency>
      <groupId>org.keycloak</groupId>
      <artifactId>keycloak-server-spi-private</artifactId>
      <version>${keycloak.version}</version>
      <scope>provided</scope>
    </dependency>
    <dependency>
      <groupId>org.keycloak</groupId>
      <artifactId>keycloak-services</artifactId>
      <version>${keycloak.version}</version>
      <scope>provided</scope>
    </dependency>
    <dependency>
      <groupId>org.junit.jupiter</groupId>
      <artifactId>junit-jupiter</artifactId>
      <version>${junit.version}</version>
      <scope>test</scope>
    </dependency>
    <dependency>
      <groupId>org.mockito</groupId>
      <artifactId>mockito-core</artifactId>
      <version>${mockito.version}</version>
      <scope>test</scope>
    </dependency>
  </dependencies>

  <build>
    <finalName>lantern-keycloak-extensions</finalName>
    <plugins>
      <plugin>
        <groupId>org.apache.maven.plugins</groupId>
        <artifactId>maven-surefire-plugin</artifactId>
        <version>3.5.2</version>
      </plugin>
    </plugins>
  </build>
</project>
```

- [ ] **Step 2: Write the failing test**

`keycloak/pin-authenticator/src/test/java/dev/lantern/keycloak/OutletIdMapperTest.java`:
```java
package dev.lantern.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import java.util.Optional;
import java.util.ServiceLoader;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.keycloak.models.GroupModel;
import org.keycloak.protocol.ProtocolMapper;

class OutletIdMapperTest {

    private static GroupModel group(String outletId) {
        GroupModel g = mock(GroupModel.class);
        when(g.getFirstAttribute(OutletIdMapper.ATTRIBUTE)).thenReturn(outletId);
        return g;
    }

    @Test
    void returnsOutletIdOfTheOutletGroup() {
        assertEquals(Optional.of("BGS"), OutletIdMapper.resolveOutletId(Stream.of(group(null), group("BGS"))));
    }

    @Test
    void emptyWhenNoGroupCarriesAnOutletId() {
        assertEquals(Optional.empty(), OutletIdMapper.resolveOutletId(Stream.of(group(null), group(null))));
    }

    @Test
    void skipsBlankValues() {
        assertEquals(Optional.of("KLC"), OutletIdMapper.resolveOutletId(Stream.of(group("  "), group("KLC"))));
    }

    @Test
    void isRegisteredAsAProtocolMapper() {
        assertTrue(ServiceLoader.load(ProtocolMapper.class).stream()
                .anyMatch(p -> p.type() == OutletIdMapper.class));
    }
}
```

- [ ] **Step 3: Run the test and confirm it fails**

```bash
docker run --rm -v lantern-m2:/root/.m2 -v "$PWD/keycloak/pin-authenticator":/build -w /build \
  maven:3.9-eclipse-temurin-21 mvn -q -B test
```
Expected: FAIL with compilation error `cannot find symbol: class OutletIdMapper`.

- [ ] **Step 4: Implement the mapper and register it**

`keycloak/pin-authenticator/src/main/java/dev/lantern/keycloak/OutletIdMapper.java`:
```java
package dev.lantern.keycloak;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.Optional;
import java.util.stream.Stream;
import org.keycloak.models.ClientSessionContext;
import org.keycloak.models.GroupModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.ProtocolMapperModel;
import org.keycloak.models.UserSessionModel;
import org.keycloak.protocol.oidc.mappers.AbstractOIDCProtocolMapper;
import org.keycloak.protocol.oidc.mappers.OIDCAccessTokenMapper;
import org.keycloak.protocol.oidc.mappers.OIDCAttributeMapperHelper;
import org.keycloak.protocol.oidc.mappers.OIDCIDTokenMapper;
import org.keycloak.protocol.oidc.mappers.UserInfoTokenMapper;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.representations.IDToken;

/** Copies the {@code outlet_id} attribute of the user's outlet group into a token claim. */
public class OutletIdMapper extends AbstractOIDCProtocolMapper
        implements OIDCAccessTokenMapper, OIDCIDTokenMapper, UserInfoTokenMapper {

    public static final String PROVIDER_ID = "lantern-outlet-id-mapper";
    public static final String ATTRIBUTE = "outlet_id";

    private static final List<ProviderConfigProperty> CONFIG = new ArrayList<>();

    static {
        OIDCAttributeMapperHelper.addTokenClaimNameConfig(CONFIG);
        OIDCAttributeMapperHelper.addIncludeInTokensConfig(CONFIG, OutletIdMapper.class);
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayCategory() {
        return TOKEN_MAPPER_CATEGORY;
    }

    @Override
    public String getDisplayType() {
        return "Lantern outlet id";
    }

    @Override
    public String getHelpText() {
        return "Adds the outlet_id attribute of the user's outlet group as a claim.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return CONFIG;
    }

    @Override
    protected void setClaim(IDToken token, ProtocolMapperModel mappingModel, UserSessionModel userSession,
                            KeycloakSession keycloakSession, ClientSessionContext clientSessionCtx) {
        resolveOutletId(userSession.getUser().getGroupsStream())
                .ifPresent(outletId -> OIDCAttributeMapperHelper.mapClaim(token, mappingModel, outletId));
    }

    static Optional<String> resolveOutletId(Stream<GroupModel> groups) {
        return groups
                .map(g -> g.getFirstAttribute(ATTRIBUTE))
                .filter(Objects::nonNull)
                .filter(v -> !v.isBlank())
                .findFirst();
    }
}
```

`keycloak/pin-authenticator/src/main/resources/META-INF/services/org.keycloak.protocol.ProtocolMapper`:
```
dev.lantern.keycloak.OutletIdMapper
```

- [ ] **Step 5: Run the test and confirm it passes**

Run the same `docker run … mvn -q -B test` command as Step 3.
Expected: `Tests run: 4, Failures: 0, Errors: 0` (or no output with `-q` and exit code 0).

- [ ] **Step 6: Create the Keycloak image**

`keycloak/.dockerignore`:
```
pin-authenticator/target
```

`keycloak/Dockerfile`:
```dockerfile
FROM maven:3.9-eclipse-temurin-21 AS plugin
WORKDIR /build
COPY pin-authenticator/pom.xml .
RUN mvn -q -B dependency:go-offline
COPY pin-authenticator/src ./src
RUN mvn -q -B package

FROM quay.io/keycloak/keycloak:26.7.5
COPY --from=plugin /build/target/lantern-keycloak-extensions.jar /opt/keycloak/providers/
```

Run: `docker build -t lantern-keycloak:dev keycloak && docker run --rm --entrypoint ls lantern-keycloak:dev /opt/keycloak/providers`
Expected: build succeeds, with the plugin tests running inside `mvn package`, and the listing shows `lantern-keycloak-extensions.jar`.

- [ ] **Step 7: Commit**

```bash
git add keycloak/pin-authenticator keycloak/Dockerfile keycloak/.dockerignore
git commit -m "feat: keycloak extension jar with outlet_id mapper and image"
```

---

### Task 3: Realm, Docker Compose, and the hostname spike

**Files:**
- Create: `keycloak/realm/lantern-realm.json`
- Create: `docker-compose.yml`
- Test: `scripts/verify-hostname.sh`

**Interfaces:**
- Consumes: Keycloak image from Task 2, mapper id `lantern-outlet-id-mapper`.
- Produces, used by every later task:
  - Realm `lantern`. Roles `hq-admin`, `hq-staff`, `marketing`, `procurement`, `finance`, `outlet-manager`, `outlet-device`, `cashier`.
  - Groups `/HQ/Admin`, `/HQ/Marketing`, `/HQ/Procurement`, `/HQ/Finance`, and `/Outlets/Bangsar|KLCC|PJ` with `outlet_id` `BGS|KLC|PJY`.
  - Clients `backoffice`, `outlet-admin`, `till` (each with the three mappers below), `lantern-api` (secret `lantern-api-dev-secret`), `api-admin-svc` (secret `api-admin-svc-dev-secret`, service account with `manage-users`, `view-users`, `query-groups`, `query-users`).
  - Token claims `aud` ⊇ `lantern-api`, `roles` (array), `outlet_id`.
  - The users from spec §4.7. Cashiers have no credentials yet; PINs come in Plan 2.
  - Compose network `lantern_default`.

- [ ] **Step 1: Write the failing check**

`scripts/verify-hostname.sh`:
```bash
#!/usr/bin/env bash
# Verifies spec §8: one issuer for the browser and for containers, with back-channel
# endpoints staying on the Docker network.
set -euo pipefail

expected="http://localhost:8080/realms/lantern"
network="lantern_default"
fail() { echo "FAIL: $*" >&2; exit 1; }
json() { python3 -c "import sys,json; print(json.load(sys.stdin)$1)"; }
in_net() { docker run --rm --network "$network" curlimages/curl -fsS "$@"; }

host_cfg=$(curl -fsS "$expected/.well-known/openid-configuration") || fail "Keycloak not reachable on localhost:8080"
[[ "$(json "['issuer']" <<<"$host_cfg")" == "$expected" ]] || fail "host issuer mismatch"

net_cfg=$(in_net "http://keycloak:8080/realms/lantern/.well-known/openid-configuration")
[[ "$(json "['issuer']" <<<"$net_cfg")" == "$expected" ]] || fail "container issuer mismatch"
[[ "$(json "['token_endpoint']" <<<"$net_cfg")" == http://keycloak:8080/* ]] || fail "back-channel token endpoint left the Docker network"
[[ "$(json "['authorization_endpoint']" <<<"$net_cfg")" == http://localhost:8080/* ]] || fail "front-channel authorization endpoint is not localhost:8080"

token=$(in_net -d grant_type=client_credentials -d client_id=api-admin-svc -d client_secret=api-admin-svc-dev-secret \
  "http://keycloak:8080/realms/lantern/protocol/openid-connect/token" | json "['access_token']")
iss=$(python3 -c "import sys,json,base64; p=sys.argv[1].split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p))['iss'])" "$token")
[[ "$iss" == "$expected" ]] || fail "token minted inside the network has iss=$iss"

echo "OK: issuer is $expected from host and containers; back-channel stays on keycloak:8080"
```

```bash
chmod +x scripts/verify-hostname.sh
./scripts/verify-hostname.sh
```
Expected: `FAIL: Keycloak not reachable on localhost:8080`.

- [ ] **Step 2: Write the realm**

`keycloak/realm/lantern-realm.json`:
```json
{
  "realm": "lantern",
  "enabled": true,
  "displayName": "Lantern Mart",
  "sslRequired": "external",
  "accessTokenLifespan": 300,
  "ssoSessionIdleTimeout": 1800,
  "ssoSessionMaxLifespan": 36000,
  "offlineSessionIdleTimeout": 2592000,
  "offlineSessionMaxLifespanEnabled": false,
  "bruteForceProtected": true,
  "failureFactor": 5,
  "permanentLockout": false,
  "waitIncrementSeconds": 60,
  "maxFailureWaitSeconds": 900,
  "maxDeltaTimeSeconds": 43200,
  "smtpServer": {
    "host": "mailpit",
    "port": "1025",
    "from": "no-reply@lantern.test",
    "fromDisplayName": "Lantern Mart"
  },
  "roles": {
    "realm": [
      { "name": "hq-admin", "description": "Manages staff; reads everything; MFA required" },
      { "name": "hq-staff", "description": "Base HQ role: dashboard and read-only reports" },
      { "name": "marketing", "description": "Creates and schedules promotions" },
      { "name": "procurement", "description": "Raises purchase orders, reads stock and suppliers" },
      { "name": "finance", "description": "Reads sales reports, approves purchase orders" },
      { "name": "outlet-manager", "description": "Manages one outlet in Outlet Admin" },
      { "name": "outlet-device", "description": "Identifies one till of an outlet" },
      { "name": "cashier", "description": "Rings sales at a till; PIN only" }
    ]
  },
  "groups": [
    {
      "name": "HQ",
      "subGroups": [
        { "name": "Admin", "realmRoles": ["hq-admin", "hq-staff"] },
        { "name": "Marketing", "realmRoles": ["hq-staff", "marketing"] },
        { "name": "Procurement", "realmRoles": ["hq-staff", "procurement"] },
        { "name": "Finance", "realmRoles": ["hq-staff", "finance"] }
      ]
    },
    {
      "name": "Outlets",
      "subGroups": [
        { "name": "Bangsar", "attributes": { "outlet_id": ["BGS"] } },
        { "name": "KLCC", "attributes": { "outlet_id": ["KLC"] } },
        { "name": "PJ", "attributes": { "outlet_id": ["PJY"] } }
      ]
    }
  ],
  "clients": [
    {
      "clientId": "backoffice",
      "name": "Lantern Back Office",
      "enabled": true,
      "publicClient": false,
      "secret": "backoffice-dev-secret",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "redirectUris": ["http://localhost:5200/*"],
      "webOrigins": ["http://localhost:5200"],
      "attributes": {
        "pkce.code.challenge.method": "S256",
        "post.logout.redirect.uris": "http://localhost:5200/*"
      },
      "protocolMappers": [
        { "name": "aud-lantern-api", "protocol": "openid-connect", "protocolMapper": "oidc-audience-mapper",
          "config": { "included.client.audience": "lantern-api", "access.token.claim": "true", "id.token.claim": "false", "introspection.token.claim": "true" } },
        { "name": "roles", "protocol": "openid-connect", "protocolMapper": "oidc-usermodel-realm-role-mapper",
          "config": { "claim.name": "roles", "multivalued": "true", "jsonType.label": "String", "access.token.claim": "true", "id.token.claim": "true", "userinfo.token.claim": "true", "introspection.token.claim": "true" } },
        { "name": "outlet_id", "protocol": "openid-connect", "protocolMapper": "lantern-outlet-id-mapper",
          "config": { "claim.name": "outlet_id", "jsonType.label": "String", "access.token.claim": "true", "id.token.claim": "true", "userinfo.token.claim": "true" } }
      ]
    },
    {
      "clientId": "outlet-admin",
      "name": "Lantern Outlet Admin",
      "enabled": true,
      "publicClient": false,
      "secret": "outlet-admin-dev-secret",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "redirectUris": ["http://localhost:5300/*"],
      "webOrigins": ["http://localhost:5300"],
      "attributes": {
        "pkce.code.challenge.method": "S256",
        "post.logout.redirect.uris": "http://localhost:5300/*"
      },
      "protocolMappers": [
        { "name": "aud-lantern-api", "protocol": "openid-connect", "protocolMapper": "oidc-audience-mapper",
          "config": { "included.client.audience": "lantern-api", "access.token.claim": "true", "id.token.claim": "false", "introspection.token.claim": "true" } },
        { "name": "roles", "protocol": "openid-connect", "protocolMapper": "oidc-usermodel-realm-role-mapper",
          "config": { "claim.name": "roles", "multivalued": "true", "jsonType.label": "String", "access.token.claim": "true", "id.token.claim": "true", "userinfo.token.claim": "true", "introspection.token.claim": "true" } },
        { "name": "outlet_id", "protocol": "openid-connect", "protocolMapper": "lantern-outlet-id-mapper",
          "config": { "claim.name": "outlet_id", "jsonType.label": "String", "access.token.claim": "true", "id.token.claim": "true", "userinfo.token.claim": "true" } }
      ]
    },
    {
      "clientId": "till",
      "name": "Lantern Till",
      "enabled": true,
      "publicClient": false,
      "secret": "till-dev-secret",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "redirectUris": ["http://localhost:5400/*"],
      "webOrigins": ["http://localhost:5400"],
      "attributes": {
        "pkce.code.challenge.method": "S256",
        "post.logout.redirect.uris": "http://localhost:5400/*"
      },
      "protocolMappers": [
        { "name": "aud-lantern-api", "protocol": "openid-connect", "protocolMapper": "oidc-audience-mapper",
          "config": { "included.client.audience": "lantern-api", "access.token.claim": "true", "id.token.claim": "false", "introspection.token.claim": "true" } },
        { "name": "roles", "protocol": "openid-connect", "protocolMapper": "oidc-usermodel-realm-role-mapper",
          "config": { "claim.name": "roles", "multivalued": "true", "jsonType.label": "String", "access.token.claim": "true", "id.token.claim": "true", "userinfo.token.claim": "true", "introspection.token.claim": "true" } },
        { "name": "outlet_id", "protocol": "openid-connect", "protocolMapper": "lantern-outlet-id-mapper",
          "config": { "claim.name": "outlet_id", "jsonType.label": "String", "access.token.claim": "true", "id.token.claim": "true", "userinfo.token.claim": "true" } }
      ]
    },
    {
      "clientId": "lantern-api",
      "name": "Lantern API (audience and introspection only)",
      "enabled": true,
      "publicClient": false,
      "secret": "lantern-api-dev-secret",
      "standardFlowEnabled": false,
      "implicitFlowEnabled": false,
      "directAccessGrantsEnabled": false,
      "serviceAccountsEnabled": false
    },
    {
      "clientId": "api-admin-svc",
      "name": "Lantern API admin service account",
      "enabled": true,
      "publicClient": false,
      "secret": "api-admin-svc-dev-secret",
      "standardFlowEnabled": false,
      "implicitFlowEnabled": false,
      "directAccessGrantsEnabled": false,
      "serviceAccountsEnabled": true
    }
  ],
  "users": [
    { "username": "service-account-api-admin-svc", "enabled": true, "serviceAccountClientId": "api-admin-svc",
      "clientRoles": { "realm-management": ["manage-users", "view-users", "query-groups", "query-users"] } },

    { "username": "aisha.admin", "enabled": true, "email": "aisha.admin@lantern.test", "emailVerified": true, "firstName": "Aisha", "lastName": "Rahman",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/HQ/Admin"] },
    { "username": "ben.admin", "enabled": true, "email": "ben.admin@lantern.test", "emailVerified": true, "firstName": "Ben", "lastName": "Lim",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/HQ/Admin"] },
    { "username": "chloe.staff", "enabled": true, "email": "chloe.staff@lantern.test", "emailVerified": true, "firstName": "Chloe", "lastName": "Wong",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/HQ"], "realmRoles": ["hq-staff"] },
    { "username": "dina.marketing", "enabled": true, "email": "dina.marketing@lantern.test", "emailVerified": true, "firstName": "Dina", "lastName": "Kaur",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/HQ/Marketing"] },
    { "username": "eric.procure", "enabled": true, "email": "eric.procure@lantern.test", "emailVerified": true, "firstName": "Eric", "lastName": "Tan",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/HQ/Procurement"] },
    { "username": "farah.finance", "enabled": true, "email": "farah.finance@lantern.test", "emailVerified": true, "firstName": "Farah", "lastName": "Ismail",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/HQ/Finance"] },
    { "username": "gary.multi", "enabled": true, "email": "gary.multi@lantern.test", "emailVerified": true, "firstName": "Gary", "lastName": "Ng",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/HQ/Marketing", "/HQ/Procurement"] },
    { "username": "hana.dual", "enabled": true, "email": "hana.dual@lantern.test", "emailVerified": true, "firstName": "Hana", "lastName": "Yusof",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/HQ/Procurement", "/HQ/Finance"] },

    { "username": "mgr.bangsar", "enabled": true, "email": "mgr.bangsar@lantern.test", "emailVerified": true, "firstName": "Bangsar", "lastName": "Manager",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/Outlets/Bangsar"], "realmRoles": ["outlet-manager"] },
    { "username": "mgr.klcc", "enabled": true, "email": "mgr.klcc@lantern.test", "emailVerified": true, "firstName": "KLCC", "lastName": "Manager",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/Outlets/KLCC"], "realmRoles": ["outlet-manager"] },
    { "username": "mgr.pj", "enabled": true, "email": "mgr.pj@lantern.test", "emailVerified": true, "firstName": "PJ", "lastName": "Manager",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/Outlets/PJ"], "realmRoles": ["outlet-manager"] },

    { "username": "outlet-bangsar-1", "enabled": true, "email": "outlet-bangsar-1@lantern.test", "emailVerified": true, "firstName": "Bangsar", "lastName": "Till 1",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/Outlets/Bangsar"], "realmRoles": ["outlet-device"] },
    { "username": "outlet-bangsar-2", "enabled": true, "email": "outlet-bangsar-2@lantern.test", "emailVerified": true, "firstName": "Bangsar", "lastName": "Till 2",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/Outlets/Bangsar"], "realmRoles": ["outlet-device"] },
    { "username": "outlet-klcc-1", "enabled": true, "email": "outlet-klcc-1@lantern.test", "emailVerified": true, "firstName": "KLCC", "lastName": "Till 1",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/Outlets/KLCC"], "realmRoles": ["outlet-device"] },
    { "username": "outlet-pj-1", "enabled": true, "email": "outlet-pj-1@lantern.test", "emailVerified": true, "firstName": "PJ", "lastName": "Till 1",
      "credentials": [{ "type": "password", "value": "Lantern!2026", "temporary": false }], "groups": ["/Outlets/PJ"], "realmRoles": ["outlet-device"] },

    { "username": "c-1001", "enabled": true, "email": "c-1001@lantern.test", "emailVerified": true, "firstName": "Siti", "lastName": "Aminah",
      "groups": ["/Outlets/Bangsar"], "realmRoles": ["cashier"] },
    { "username": "c-1002", "enabled": true, "email": "c-1002@lantern.test", "emailVerified": true, "firstName": "Raj", "lastName": "Kumar",
      "groups": ["/Outlets/Bangsar"], "realmRoles": ["cashier"] },
    { "username": "c-2001", "enabled": true, "email": "c-2001@lantern.test", "emailVerified": true, "firstName": "Mei Ling", "lastName": "Chan",
      "groups": ["/Outlets/KLCC"], "realmRoles": ["cashier"] },
    { "username": "c-2002", "enabled": true, "email": "c-2002@lantern.test", "emailVerified": true, "firstName": "Daniel", "lastName": "Lee",
      "groups": ["/Outlets/KLCC"], "realmRoles": ["cashier"] },
    { "username": "c-3001", "enabled": true, "email": "c-3001@lantern.test", "emailVerified": true, "firstName": "Nurul", "lastName": "Huda",
      "groups": ["/Outlets/PJ"], "realmRoles": ["cashier"] }
  ]
}
```

- [ ] **Step 3: Write Docker Compose (infrastructure only; the API joins in Task 10)**

`docker-compose.yml`:
```yaml
name: lantern

services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: keycloak
      POSTGRES_USER: keycloak
      POSTGRES_PASSWORD: keycloak
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U keycloak -d keycloak"]
      interval: 5s
      timeout: 3s
      retries: 20

  keycloak:
    build: ./keycloak
    image: lantern-keycloak:dev
    command:
      - start
      - --import-realm
      - --http-enabled=true
      - --hostname=http://localhost:8080
      - --hostname-backchannel-dynamic=true
      - --health-enabled=true
    environment:
      KC_DB: postgres
      KC_DB_URL: jdbc:postgresql://postgres:5432/keycloak
      KC_DB_USERNAME: keycloak
      KC_DB_PASSWORD: keycloak
      KC_BOOTSTRAP_ADMIN_USERNAME: admin
      KC_BOOTSTRAP_ADMIN_PASSWORD: admin
    volumes:
      - ./keycloak/realm:/opt/keycloak/data/import:ro
    ports:
      - "8080:8080"
    depends_on:
      postgres:
        condition: service_healthy
    healthcheck:
      test:
        - CMD
        - bash
        - -c
        - >-
          exec 3<>/dev/tcp/127.0.0.1/9000 &&
          printf 'GET /health/ready HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3 &&
          grep -q '"UP"' <&3
      interval: 10s
      timeout: 5s
      retries: 30
      start_period: 30s

  mailpit:
    image: axllent/mailpit:v1.31.3
    ports:
      - "8025:8025"

volumes:
  postgres-data:
```

Note: `--import-realm` skips a realm that already exists. After editing the realm file, run `docker compose down -v` to re-import.

- [ ] **Step 4: Start the stack and run the check**

```bash
docker compose up -d --build --wait
./scripts/verify-hostname.sh
```
Expected: `OK: issuer is http://localhost:8080/realms/lantern from host and containers; back-channel stays on keycloak:8080`.

If it fails on the back-channel line, read the Keycloak 26.7 hostname guide (`https://www.keycloak.org/server/hostname`) and adjust only the `--hostname*` flags. Record what changed in the commit message. This is spec risk §12.1, and the API configuration in Task 4 depends on it.

- [ ] **Step 5: Confirm the custom mapper works in a real token**

```bash
docker compose exec keycloak /opt/keycloak/bin/kcadm.sh config credentials --config /tmp/kcadm.config --server http://localhost:8080 --realm master --user admin --password admin
docker compose exec keycloak /opt/keycloak/bin/kcadm.sh get clients --config /tmp/kcadm.config -r lantern -q clientId=backoffice --fields protocolMappers
```
Expected: three mappers listed, including `"protocolMapper" : "lantern-outlet-id-mapper"`. The claim itself is asserted end to end by Task 4's tests.

- [ ] **Step 6: Commit**

```bash
git add keycloak/realm docker-compose.yml scripts/verify-hostname.sh
git commit -m "feat: lantern realm, compose stack and hostname check"
```

---

### Task 4: JWT authentication in the API, test fixture, and `/me`

**Files:**
- Create: `src/Lantern.Api/Auth/KeycloakOptions.cs`, `src/Lantern.Api/Auth/AuthSetup.cs`, `src/Lantern.Api/Auth/Roles.cs`, `src/Lantern.Api/Endpoints/MeEndpoints.cs`
- Modify: `src/Lantern.Api/Program.cs`
- Create: `tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`, `ApiFactory.cs`, `KeycloakCollection.cs`
- Test: `tests/Lantern.IntegrationTests/AuthenticationTests.cs`

**Interfaces:**
- Consumes: realm and image from Tasks 2–3.
- Produces:
  - `KeycloakOptions` with `BaseUrl`, `Realm`, `Issuer`, `Audience`, `ApiClientId`, `ApiClientSecret`, `AdminClientId`, `AdminClientSecret`, `IntrospectionCacheSeconds`, plus computed `RealmUrl` and `AdminUrl`.
  - `IServiceCollection.AddLanternJwt(IConfiguration)`.
  - `Roles` constants: `HqAdmin`, `HqStaff`, `Marketing`, `Procurement`, `Finance`, `OutletManager`, `OutletDevice`, `Cashier`, and the set `All`.
  - `GET /me` returns `MeResponse(string Id, string Name, string[] Roles, string? OutletId)`.
  - Test infrastructure:
    - `KeycloakFixture`: `BaseUrl`, `Issuer`, `MailpitUrl`, `Api`, `Http`, `GetUserTokenAsync(username, password)`, `GetFreshUserTokenAsync(username, password)`, `AdminClientAsync()`, `CreateTempUserAsync(groupPath, password)`, `SetUserEnabledAsync(id, enabled)`, `LogoutUserAsync(id)`, `WaitForEmailCountAsync(email, atLeast, timeout)`.
    - `ApiFactory(KeycloakFixture, int introspectionCacheSeconds)`: `ClientAsAsync(username)`, `ClientWithToken(token)`.
    - `KeycloakCollection.Name == "keycloak"`.

- [ ] **Step 1: Write the test infrastructure**

`tests/Lantern.IntegrationTests/Infrastructure/KeycloakCollection.cs`:
```csharp
namespace Lantern.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class KeycloakCollection : ICollectionFixture<KeycloakFixture>
{
    public const string Name = "keycloak";
}
```

`tests/Lantern.IntegrationTests/Infrastructure/ApiFactory.cs`:
```csharp
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Lantern.IntegrationTests.Infrastructure;

public sealed class ApiFactory(KeycloakFixture kc, int introspectionCacheSeconds) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:BaseUrl"] = kc.BaseUrl,
            ["Keycloak:Issuer"] = kc.Issuer,
            ["Keycloak:IntrospectionCacheSeconds"] = introspectionCacheSeconds.ToString()
        }));

    public async Task<HttpClient> ClientAsAsync(string username) =>
        ClientWithToken(await kc.GetUserTokenAsync(username));

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
```

`tests/Lantern.IntegrationTests/Infrastructure/KeycloakFixture.cs`:
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Testcontainers.Keycloak;

namespace Lantern.IntegrationTests.Infrastructure;

/// <summary>
/// One Keycloak (our image, our realm) and one Mailpit for the whole test run.
/// Adds a test-only password-grant client so tests can get user tokens without a browser.
/// </summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
    public const string DemoPassword = "Lantern!2026";
    private const string TestClientId = "test-runner";
    private const string TestClientSecret = "test-runner-secret";

    private readonly Dictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokens = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private INetwork _network = null!;
    private IFutureDockerImage _image = null!;
    private KeycloakContainer _keycloak = null!;
    private IContainer _mailpit = null!;

    public string BaseUrl { get; private set; } = "";
    public string Issuer => $"{BaseUrl}/realms/lantern";
    public string MailpitUrl { get; private set; } = "";
    public ApiFactory Api { get; private set; } = null!;
    public HttpClient Http { get; } = new();

    public async Task InitializeAsync()
    {
        var repoRoot = CommonDirectoryPath.GetGitDirectory();

        _image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(repoRoot, "keycloak")
            .WithDockerfile("Dockerfile")
            .WithName("lantern-keycloak-test:26.7.5") // version tag lets Testcontainers pick the 26.x health port
            .WithCleanUp(false)
            .Build();
        await _image.CreateAsync();

        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        _mailpit = new ContainerBuilder()
            .WithImage("axllent/mailpit:v1.31.3")
            .WithNetwork(_network)
            .WithNetworkAliases("mailpit")
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/livez")))
            .Build();
        await _mailpit.StartAsync();
        MailpitUrl = $"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}";

        var realmPath = Path.Combine(repoRoot.DirectoryPath, "keycloak", "realm", "lantern-realm.json");
        _keycloak = new KeycloakBuilder()
            .WithImage(_image)
            .WithNetwork(_network)
            .WithResourceMapping(Encoding.UTF8.GetBytes(BuildTestRealm(realmPath)), "/opt/keycloak/data/import/lantern-realm.json")
            .WithCommand("--import-realm")
            .Build();
        await _keycloak.StartAsync();
        BaseUrl = _keycloak.GetBaseAddress().TrimEnd('/');

        Api = new ApiFactory(this, introspectionCacheSeconds: 0);
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await _keycloak.DisposeAsync();
        await _mailpit.DisposeAsync();
        await _network.DisposeAsync();
        Http.Dispose();
    }

    private static string BuildTestRealm(string path)
    {
        var realm = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var clients = realm["clients"]!.AsArray();
        var mappers = clients.First(c => (string?)c!["clientId"] == "backoffice")!["protocolMappers"]!.DeepClone();
        clients.Add(new JsonObject
        {
            ["clientId"] = TestClientId,
            ["enabled"] = true,
            ["publicClient"] = false,
            ["secret"] = TestClientSecret,
            ["standardFlowEnabled"] = false,
            ["directAccessGrantsEnabled"] = true,
            ["protocolMappers"] = mappers
        });
        return realm.ToJsonString();
    }

    /// <summary>Cached per user; refreshed when under 30 s from expiry.</summary>
    public async Task<string> GetUserTokenAsync(string username, string password = DemoPassword)
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_tokens.TryGetValue(username, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
                return cached.Token;
            var fresh = await RequestPasswordTokenAsync(username, password);
            _tokens[username] = fresh;
            return fresh.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<string> GetFreshUserTokenAsync(string username, string password = DemoPassword) =>
        (await RequestPasswordTokenAsync(username, password)).Token;

    private async Task<(string Token, DateTimeOffset ExpiresAt)> RequestPasswordTokenAsync(string username, string password)
    {
        using var response = await Http.PostAsync($"{Issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = TestClientId,
                ["client_secret"] = TestClientSecret,
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid"
            }));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token request for {username} failed: {(int)response.StatusCode} {body}");
        var json = JsonNode.Parse(body)!;
        return ((string)json["access_token"]!, DateTimeOffset.UtcNow.AddSeconds((int)json["expires_in"]!));
    }

    /// <summary>Admin API client for the lantern realm, authenticated as the master admin.</summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        using var response = await Http.PostAsync($"{BaseUrl}/realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = KeycloakBuilder.DefaultUsername,
                ["password"] = KeycloakBuilder.DefaultPassword
            }));
        response.EnsureSuccessStatusCode();
        var token = (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["access_token"]!;
        var client = new HttpClient { BaseAddress = new Uri($"{BaseUrl}/admin/realms/lantern/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<(string Id, string Username, string Email)> CreateTempUserAsync(string groupPath, string password = DemoPassword)
    {
        var username = $"temp-{Guid.NewGuid():N}"[..20];
        var email = $"{username}@lantern.test";
        using var admin = await AdminClientAsync();
        using var create = await admin.PostAsJsonAsync("users", new
        {
            username,
            enabled = true,
            email,
            emailVerified = true,
            firstName = "Temp",
            lastName = "User",
            groups = new[] { groupPath },
            credentials = new[] { new { type = "password", value = password, temporary = false } }
        });
        create.EnsureSuccessStatusCode();
        return (create.Headers.Location!.Segments.Last(), username, email);
    }

    public async Task SetUserEnabledAsync(string id, bool enabled)
    {
        using var admin = await AdminClientAsync();
        var user = (await admin.GetFromJsonAsync<JsonObject>($"users/{id}"))!;
        user["enabled"] = enabled;
        using var put = await admin.PutAsJsonAsync($"users/{id}", user);
        put.EnsureSuccessStatusCode();
    }

    public async Task LogoutUserAsync(string id)
    {
        using var admin = await AdminClientAsync();
        using var response = await admin.PostAsync($"users/{id}/logout", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int> WaitForEmailCountAsync(string email, int atLeast, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        var count = 0;
        while (DateTime.UtcNow < deadline)
        {
            var json = await Http.GetFromJsonAsync<JsonElement>(
                $"{MailpitUrl}/api/v1/search?query={Uri.EscapeDataString("to:" + email)}");
            count = json.GetProperty("messages_count").GetInt32();
            if (count >= atLeast) return count;
            await Task.Delay(500);
        }
        return count;
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Lantern.IntegrationTests/AuthenticationTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class AuthenticationTests(KeycloakFixture kc)
{
    private sealed record Me(string Id, string Name, string[] Roles, string? OutletId);

    [Fact]
    public async Task Me_without_token_is_401()
    {
        var response = await kc.Api.CreateClient().GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Outlet_manager_token_carries_role_and_outlet_id()
    {
        var client = await kc.Api.ClientAsAsync("mgr.bangsar");

        var me = await client.GetFromJsonAsync<Me>("/me");

        Assert.Equal("mgr.bangsar", me!.Name);
        Assert.Contains("outlet-manager", me.Roles);
        Assert.Equal("BGS", me.OutletId);
        Assert.False(string.IsNullOrEmpty(me.Id));
    }

    [Fact]
    public async Task Hq_user_gets_roles_from_department_group_and_no_outlet()
    {
        var client = await kc.Api.ClientAsAsync("eric.procure");

        var me = await client.GetFromJsonAsync<Me>("/me");

        Assert.Equal(new[] { "hq-staff", "procurement" }, me!.Roles);
        Assert.Null(me.OutletId);
    }

    [Fact]
    public async Task Token_from_another_realm_is_401()
    {
        using var masterToken = await kc.Http.PostAsync($"{kc.BaseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = "admin", ["password"] = "admin"
            }));
        var token = (string)JsonNode.Parse(await masterToken.Content.ReadAsStringAsync())!["access_token"]!;

        var response = await kc.Api.ClientWithToken(token).GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Garbage_token_is_401()
    {
        var response = await kc.Api.ClientWithToken("not-a-jwt").GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~AuthenticationTests`
Expected: FAIL. `/me` returns 404, so the `Me_without_token_is_401` assertion fails and `GetFromJsonAsync` throws. The first run also builds the Keycloak test image, which takes a few minutes.

- [ ] **Step 4: Implement authentication and `/me`**

`src/Lantern.Api/Auth/KeycloakOptions.cs`:
```csharp
namespace Lantern.Api.Auth;

public sealed class KeycloakOptions
{
    public const string Section = "Keycloak";

    /// <summary>Back-channel base URL the API uses to reach Keycloak (e.g. http://keycloak:8080).</summary>
    public string BaseUrl { get; set; } = "";
    public string Realm { get; set; } = "lantern";
    /// <summary>Exact iss value in tokens; differs from BaseUrl inside Docker (spec §8).</summary>
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "lantern-api";
    public string ApiClientId { get; set; } = "lantern-api";
    public string ApiClientSecret { get; set; } = "";
    public string AdminClientId { get; set; } = "api-admin-svc";
    public string AdminClientSecret { get; set; } = "";
    public int IntrospectionCacheSeconds { get; set; } = 15;

    public string RealmUrl => $"{BaseUrl.TrimEnd('/')}/realms/{Realm}";
    public string AdminUrl => $"{BaseUrl.TrimEnd('/')}/admin/realms/{Realm}";
}
```

`src/Lantern.Api/Auth/Roles.cs`:
```csharp
namespace Lantern.Api.Auth;

public static class Roles
{
    public const string HqAdmin = "hq-admin";
    public const string HqStaff = "hq-staff";
    public const string Marketing = "marketing";
    public const string Procurement = "procurement";
    public const string Finance = "finance";
    public const string OutletManager = "outlet-manager";
    public const string OutletDevice = "outlet-device";
    public const string Cashier = "cashier";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        HqAdmin, HqStaff, Marketing, Procurement, Finance, OutletManager, OutletDevice, Cashier
    };
}
```

`src/Lantern.Api/Auth/AuthSetup.cs`:
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Lantern.Api.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddLanternJwt(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<KeycloakOptions>(config.GetSection(KeycloakOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<KeycloakOptions>>((o, kcOptions) =>
            {
                var kc = kcOptions.Value;
                o.MetadataAddress = $"{kc.RealmUrl}/.well-known/openid-configuration";
                o.RequireHttpsMetadata = false;
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidIssuer = kc.Issuer;
                o.TokenValidationParameters.ValidAudience = kc.Audience;
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.TokenValidationParameters.RoleClaimType = "roles";
            });

        return services;
    }
}
```

`src/Lantern.Api/Endpoints/MeEndpoints.cs`:
```csharp
using System.Security.Claims;
using Lantern.Api.Auth;

namespace Lantern.Api.Endpoints;

public sealed record MeResponse(string Id, string Name, string[] Roles, string? OutletId);

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new MeResponse(
                user.FindFirstValue("sub") ?? "",
                user.Identity?.Name ?? "",
                user.FindAll("roles").Select(c => c.Value).Where(Roles.All.Contains).Order().ToArray(),
                user.FindFirstValue("outlet_id"))))
            .RequireAuthorization();
}
```

`src/Lantern.Api/Program.cs`:
```csharp
using Lantern.Api.Auth;
using Lantern.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddLanternJwt(builder.Configuration);
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMeEndpoints();

app.Run();

public partial class Program;
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter "FullyQualifiedName~AuthenticationTests|FullyQualifiedName~HealthTests"`
Expected: PASS (6 tests). If `Outlet_manager_token_carries_role_and_outlet_id` fails only on `OutletId`, the custom mapper isn't loaded: check the jar is in the test image (`docker run --rm --entrypoint ls lantern-keycloak-test:26.7.5 /opt/keycloak/providers`).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: api jwt validation against keycloak with /me and test fixture"
```

---

### Task 5: Role policies, HQ endpoints, and a 403 body that names the policy

**Files:**
- Create: `src/Lantern.Api/Auth/Policies.cs`, `src/Lantern.Api/Auth/ForbiddenResultHandler.cs`, `src/Lantern.Api/Auth/ClaimsPrincipalExtensions.cs`
- Create: `src/Lantern.Api/Data/DemoStore.cs`, `src/Lantern.Api/Endpoints/HqEndpoints.cs`
- Modify: `src/Lantern.Api/Program.cs`
- Test: `tests/Lantern.IntegrationTests/RolePolicyTests.cs`

**Interfaces:**
- Consumes: `Roles`, `AddLanternJwt` (Task 4), `KeycloakFixture`/`ApiFactory`.
- Produces:
  - `Policies` constants: `HqRead`, `PromotionsRead`, `Marketing`, `ProcurementRead`, `Procurement`, `PurchaseOrdersRead`, `SalesReports`, `FinanceRole`, `FinanceApprove`, `OutletManage`, `StaffAdmin`. `FinanceApprove` and `OutletManage` are registered in Tasks 6–7.
  - `IServiceCollection.AddLanternPolicies()`.
  - `ClaimsPrincipal.GetSub()` and `ClaimsPrincipal.GetDisplayName()`.
  - `DemoStore` (singleton): `OutletIds`, `Promotions`, `AddPromotion(name, startsOn, createdBy)`, `Suppliers`, `Stock`, `SalesReport`, `Roster`. Purchase-order members are added in Task 6.
  - A 403 response with ProblemDetails extension `policy` holding the endpoint's policy name(s), comma-separated.

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/RolePolicyTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class RolePolicyTests(KeycloakFixture kc)
{
    [Theory]
    [InlineData("chloe.staff", "/dashboard", HttpStatusCode.OK)]
    [InlineData("aisha.admin", "/dashboard", HttpStatusCode.OK)]
    [InlineData("mgr.bangsar", "/dashboard", HttpStatusCode.Forbidden)]
    [InlineData("outlet-bangsar-1", "/dashboard", HttpStatusCode.Forbidden)]
    [InlineData("chloe.staff", "/promotions", HttpStatusCode.OK)]
    [InlineData("mgr.bangsar", "/promotions", HttpStatusCode.OK)]
    [InlineData("outlet-bangsar-1", "/promotions", HttpStatusCode.Forbidden)]
    [InlineData("eric.procure", "/suppliers", HttpStatusCode.OK)]
    [InlineData("aisha.admin", "/suppliers", HttpStatusCode.OK)]
    [InlineData("dina.marketing", "/suppliers", HttpStatusCode.Forbidden)]
    [InlineData("eric.procure", "/stock", HttpStatusCode.OK)]
    [InlineData("chloe.staff", "/stock", HttpStatusCode.Forbidden)]
    [InlineData("farah.finance", "/reports/sales", HttpStatusCode.OK)]
    [InlineData("aisha.admin", "/reports/sales", HttpStatusCode.OK)]
    [InlineData("eric.procure", "/reports/sales", HttpStatusCode.Forbidden)]
    public async Task Read_endpoints_follow_roles(string user, string path, HttpStatusCode expected)
    {
        var client = await kc.Api.ClientAsAsync(user);
        var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("dina.marketing", HttpStatusCode.Created)]
    [InlineData("gary.multi", HttpStatusCode.Created)]
    [InlineData("chloe.staff", HttpStatusCode.Forbidden)]
    [InlineData("aisha.admin", HttpStatusCode.Forbidden)]
    [InlineData("mgr.bangsar", HttpStatusCode.Forbidden)]
    public async Task Only_marketing_creates_promotions(string user, HttpStatusCode expected)
    {
        var client = await kc.Api.ClientAsAsync(user);
        var response = await client.PostAsJsonAsync("/promotions", new { name = "Raya Week", startsOn = "2026-11-01" });
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Created_promotion_records_who_created_it()
    {
        var client = await kc.Api.ClientAsAsync("dina.marketing");
        var response = await client.PostAsJsonAsync("/promotions", new { name = "Deepavali Deals", startsOn = "2026-11-08" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Dina Kaur", body.GetProperty("createdBy").GetString());
    }

    [Fact]
    public async Task Forbidden_response_names_the_policy()
    {
        var client = await kc.Api.ClientAsAsync("chloe.staff");
        var response = await client.PostAsJsonAsync("/promotions", new { name = "X", startsOn = "2026-11-01" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Marketing", problem.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task Promotion_without_name_is_400()
    {
        var client = await kc.Api.ClientAsAsync("dina.marketing");
        var response = await client.PostAsJsonAsync("/promotions", new { name = " ", startsOn = "2026-11-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~RolePolicyTests`
Expected: FAIL, with 404s where 200/403/201 are expected.

- [ ] **Step 3: Implement policies, store and endpoints**

`src/Lantern.Api/Auth/ClaimsPrincipalExtensions.cs`:
```csharp
using System.Security.Claims;

namespace Lantern.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string GetSub(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("Token has no sub claim.");

    public static string GetDisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue("name") ?? user.Identity?.Name ?? "unknown";
}
```

`src/Lantern.Api/Auth/ForbiddenResultHandler.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Lantern.Api.Auth;

/// <summary>Turns authorization failures into a ProblemDetails 403 that names the policy (scenario C demo).</summary>
public sealed class ForbiddenResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var names = context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy).OfType<string>().ToArray() ?? [];
            await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You don't have permission to do this.",
                    extensions: new Dictionary<string, object?> { ["policy"] = string.Join(",", names) })
                .ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
```

`src/Lantern.Api/Auth/Policies.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Auth;

public static class Policies
{
    public const string HqRead = "HqRead";
    public const string PromotionsRead = "PromotionsRead";
    public const string Marketing = "Marketing";
    public const string ProcurementRead = "ProcurementRead";
    public const string Procurement = "Procurement";
    public const string PurchaseOrdersRead = "PurchaseOrdersRead";
    public const string SalesReports = "SalesReports";
    public const string FinanceRole = "FinanceRole";
    public const string FinanceApprove = "FinanceApprove";
    public const string OutletManage = "OutletManage";
    public const string StaffAdmin = "StaffAdmin";

    public static IServiceCollection AddLanternPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenResultHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(HqRead, p => p.RequireRole(Roles.HqStaff, Roles.HqAdmin))
            .AddPolicy(PromotionsRead, p => p.RequireRole(Roles.HqStaff, Roles.HqAdmin, Roles.OutletManager))
            .AddPolicy(Marketing, p => p.RequireRole(Roles.Marketing))
            .AddPolicy(ProcurementRead, p => p.RequireRole(Roles.Procurement, Roles.HqAdmin))
            .AddPolicy(Procurement, p => p.RequireRole(Roles.Procurement))
            .AddPolicy(PurchaseOrdersRead, p => p.RequireRole(Roles.Procurement, Roles.Finance, Roles.HqAdmin))
            .AddPolicy(SalesReports, p => p.RequireRole(Roles.Finance, Roles.HqAdmin))
            .AddPolicy(FinanceRole, p => p.RequireRole(Roles.Finance))
            .AddPolicy(StaffAdmin, p => p.RequireRole(Roles.HqAdmin));

        return services;
    }
}
```

`src/Lantern.Api/Data/DemoStore.cs`:
```csharp
using System.Collections.Concurrent;

namespace Lantern.Api.Data;

public sealed record Promotion(Guid Id, string Name, DateOnly StartsOn, string CreatedBy);
public sealed record Supplier(string Id, string Name);
public sealed record StockLine(string Sku, string Name, int Quantity);
public sealed record RosterEntry(string Name, string Role);
public sealed record OutletSales(string OutletId, decimal Total);

/// <summary>In-memory business data, seeded on start. Exists only to make authorization visible.</summary>
public sealed class DemoStore
{
    public static readonly string[] OutletIds = ["BGS", "KLC", "PJY"];

    private readonly ConcurrentDictionary<Guid, Promotion> _promotions = new();

    public IReadOnlyList<Promotion> Promotions => _promotions.Values.OrderBy(p => p.StartsOn).ToList();

    public Promotion AddPromotion(string name, DateOnly startsOn, string createdBy)
    {
        var promotion = new Promotion(Guid.NewGuid(), name, startsOn, createdBy);
        _promotions[promotion.Id] = promotion;
        return promotion;
    }

    public IReadOnlyList<Supplier> Suppliers { get; } =
    [
        new("SUP-01", "Kopi Beans Trading"),
        new("SUP-02", "Fresh Dairy Co"),
        new("SUP-03", "Packaging Plus")
    ];

    public IReadOnlyDictionary<string, IReadOnlyList<StockLine>> Stock { get; } =
        new Dictionary<string, IReadOnlyList<StockLine>>(StringComparer.OrdinalIgnoreCase)
        {
            ["BGS"] = [new("SKU-100", "House Blend 1kg", 42), new("SKU-200", "Oat Milk 1L", 18)],
            ["KLC"] = [new("SKU-100", "House Blend 1kg", 30), new("SKU-200", "Oat Milk 1L", 25)],
            ["PJY"] = [new("SKU-100", "House Blend 1kg", 12), new("SKU-200", "Oat Milk 1L", 7)]
        };

    public IReadOnlyDictionary<string, IReadOnlyList<RosterEntry>> Roster { get; } =
        new Dictionary<string, IReadOnlyList<RosterEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            ["BGS"] = [new("Siti Aminah", "cashier"), new("Raj Kumar", "cashier")],
            ["KLC"] = [new("Mei Ling Chan", "cashier"), new("Daniel Lee", "cashier")],
            ["PJY"] = [new("Nurul Huda", "cashier")]
        };

    public IReadOnlyList<OutletSales> SalesReport { get; } =
        [new("BGS", 18250.40m), new("KLC", 22410.00m), new("PJY", 9120.75m)];
}
```

`src/Lantern.Api/Endpoints/HqEndpoints.cs`:
```csharp
using System.Security.Claims;
using Lantern.Api.Auth;
using Lantern.Api.Data;

namespace Lantern.Api.Endpoints;

public sealed record NewPromotion(string? Name, DateOnly StartsOn);

public static class HqEndpoints
{
    public static void MapHqEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard", (ClaimsPrincipal user) =>
                Results.Ok(new { greeting = $"Hello, {user.GetDisplayName()}", outlets = DemoStore.OutletIds }))
            .RequireAuthorization(Policies.HqRead);

        app.MapGet("/promotions", (DemoStore store) => Results.Ok(store.Promotions))
            .RequireAuthorization(Policies.PromotionsRead);

        app.MapPost("/promotions", (NewPromotion body, ClaimsPrincipal user, DemoStore store) =>
            {
                if (string.IsNullOrWhiteSpace(body.Name))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
                var promotion = store.AddPromotion(body.Name.Trim(), body.StartsOn, user.GetDisplayName());
                return Results.Created($"/promotions/{promotion.Id}", promotion);
            })
            .RequireAuthorization(Policies.Marketing);

        app.MapGet("/suppliers", (DemoStore store) => Results.Ok(store.Suppliers))
            .RequireAuthorization(Policies.ProcurementRead);

        app.MapGet("/stock", (DemoStore store) => Results.Ok(store.Stock))
            .RequireAuthorization(Policies.ProcurementRead);

        app.MapGet("/reports/sales", (DemoStore store) => Results.Ok(store.SalesReport))
            .RequireAuthorization(Policies.SalesReports);
    }
}
```

`src/Lantern.Api/Program.cs`: replace `builder.Services.AddAuthorization();` with these two lines:
```csharp
builder.Services.AddLanternPolicies();
builder.Services.AddSingleton<Lantern.Api.Data.DemoStore>();
```
and add after `app.MapMeEndpoints();`:
```csharp
app.MapHqEndpoints();
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~RolePolicyTests`
Expected: PASS (23 tests).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: role policies and hq endpoints with policy-naming 403"
```

---

### Task 6: Purchase orders and separation of duties

**Files:**
- Create: `src/Lantern.Api/Auth/NotCreatorRequirement.cs`, `src/Lantern.Api/Endpoints/PurchaseOrderEndpoints.cs`
- Modify: `src/Lantern.Api/Data/DemoStore.cs`, `src/Lantern.Api/Auth/Policies.cs`, `src/Lantern.Api/Program.cs`
- Test: `tests/Lantern.IntegrationTests/PurchaseOrderApprovalTests.cs`

**Interfaces:**
- Consumes: `Policies`, `GetSub`, `GetDisplayName`, `DemoStore` (Task 5).
- Produces:
  - `PurchaseOrder(Guid Id, string SupplierId, decimal Amount, string CreatedBySub, string CreatedByName, string Status, string? ApprovedByName)`, where `Status` is `"Pending"` or `"Approved"`.
  - `DemoStore.PurchaseOrders`, `AddPurchaseOrder(...)`, `FindPurchaseOrder(Guid)`, `TryApprove(Guid, string approverName)`.
  - Routes: `GET /purchase-orders`, `POST /purchase-orders`, `POST /purchase-orders/{id}/approve`.

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/PurchaseOrderApprovalTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class PurchaseOrderApprovalTests(KeycloakFixture kc)
{
    private async Task<JsonElement> RaiseAsync(string user, string supplierId = "SUP-01", decimal amount = 1200.50m)
    {
        var client = await kc.Api.ClientAsAsync(user);
        var response = await client.PostAsJsonAsync("/purchase-orders", new { supplierId, amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> ApproveAsync(string user, string id) =>
        await (await kc.Api.ClientAsAsync(user)).PostAsync($"/purchase-orders/{id}/approve", null);

    [Fact]
    public async Task Procurement_raises_and_finance_approves()
    {
        var po = await RaiseAsync("eric.procure");
        Assert.Equal("Pending", po.GetProperty("status").GetString());
        Assert.Equal("Eric Tan", po.GetProperty("createdByName").GetString());

        var response = await ApproveAsync("farah.finance", po.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("Farah Ismail", approved.GetProperty("approvedByName").GetString());
    }

    [Fact]
    public async Task Dual_role_user_cannot_approve_own_order()
    {
        var po = await RaiseAsync("hana.dual");

        var response = await ApproveAsync("hana.dual", po.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FinanceApprove", problem.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task Dual_role_user_can_approve_someone_elses_order()
    {
        var po = await RaiseAsync("eric.procure");
        var response = await ApproveAsync("hana.dual", po.GetProperty("id").GetString()!);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Procurement_without_finance_cannot_approve()
    {
        var po = await RaiseAsync("eric.procure");

        var response = await ApproveAsync("eric.procure", po.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FinanceRole", problem.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task Second_approval_is_409_and_keeps_first_approver()
    {
        var po = await RaiseAsync("eric.procure");
        var id = po.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync("farah.finance", id)).StatusCode);

        var second = await ApproveAsync("hana.dual", id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var list = await (await kc.Api.ClientAsAsync("farah.finance")).GetFromJsonAsync<JsonElement>("/purchase-orders");
        var stored = list.EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
        Assert.Equal("Farah Ismail", stored.GetProperty("approvedByName").GetString());
    }

    [Fact]
    public async Task Unknown_order_is_404()
    {
        var response = await ApproveAsync("farah.finance", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("SUP-99", 100)]
    [InlineData("SUP-01", 0)]
    [InlineData("SUP-01", -5)]
    public async Task Invalid_order_is_400(string supplierId, decimal amount)
    {
        var client = await kc.Api.ClientAsAsync("eric.procure");
        var response = await client.PostAsJsonAsync("/purchase-orders", new { supplierId, amount });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("dina.marketing", HttpStatusCode.Forbidden)]
    [InlineData("farah.finance", HttpStatusCode.OK)]
    [InlineData("aisha.admin", HttpStatusCode.OK)]
    public async Task Listing_orders_follows_roles(string user, HttpStatusCode expected)
    {
        var response = await (await kc.Api.ClientAsAsync(user)).GetAsync("/purchase-orders");
        Assert.Equal(expected, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~PurchaseOrderApprovalTests`
Expected: FAIL (404 on `/purchase-orders`).

- [ ] **Step 3: Implement**

`src/Lantern.Api/Auth/NotCreatorRequirement.cs`:
```csharp
using System.Security.Claims;
using Lantern.Api.Data;
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Auth;

/// <summary>Separation of duties: whoever raised a purchase order may not approve it.</summary>
public sealed class NotCreatorRequirement : IAuthorizationRequirement;

public sealed class NotCreatorHandler : AuthorizationHandler<NotCreatorRequirement, PurchaseOrder>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        NotCreatorRequirement requirement, PurchaseOrder resource)
    {
        var sub = context.User.FindFirstValue("sub");
        if (sub is not null && sub != resource.CreatedBySub)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
```

`src/Lantern.Api/Auth/Policies.cs`: add the handler registration as the first line of `AddLanternPolicies`:
```csharp
        services.AddSingleton<IAuthorizationHandler, NotCreatorHandler>();
```
and add this policy after the `FinanceRole` line:
```csharp
            .AddPolicy(FinanceApprove, p => p.RequireRole(Roles.Finance).AddRequirements(new NotCreatorRequirement()))
```

`src/Lantern.Api/Data/DemoStore.cs`: add the record next to the other records:
```csharp
public sealed record PurchaseOrder(Guid Id, string SupplierId, decimal Amount, string CreatedBySub,
    string CreatedByName, string Status, string? ApprovedByName);
```
and add these members inside `DemoStore`:
```csharp
    private readonly ConcurrentDictionary<Guid, PurchaseOrder> _orders = new();

    public IReadOnlyList<PurchaseOrder> PurchaseOrders => _orders.Values.ToList();

    public PurchaseOrder AddPurchaseOrder(string supplierId, decimal amount, string createdBySub, string createdByName)
    {
        var po = new PurchaseOrder(Guid.NewGuid(), supplierId, amount, createdBySub, createdByName, "Pending", null);
        _orders[po.Id] = po;
        return po;
    }

    public PurchaseOrder? FindPurchaseOrder(Guid id) => _orders.GetValueOrDefault(id);

    /// <summary>Atomic Pending → Approved. Returns null if the order is missing or already decided.</summary>
    public PurchaseOrder? TryApprove(Guid id, string approverName)
    {
        while (_orders.TryGetValue(id, out var current))
        {
            if (current.Status != "Pending") return null;
            var approved = current with { Status = "Approved", ApprovedByName = approverName };
            if (_orders.TryUpdate(id, approved, current)) return approved;
        }
        return null;
    }
```

`src/Lantern.Api/Endpoints/PurchaseOrderEndpoints.cs`:
```csharp
using System.Security.Claims;
using Lantern.Api.Auth;
using Lantern.Api.Data;
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Endpoints;

public sealed record NewPurchaseOrder(string? SupplierId, decimal Amount);

public static class PurchaseOrderEndpoints
{
    public static void MapPurchaseOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/purchase-orders");

        orders.MapGet("/", (DemoStore store) => Results.Ok(store.PurchaseOrders))
            .RequireAuthorization(Policies.PurchaseOrdersRead);

        orders.MapPost("/", (NewPurchaseOrder body, ClaimsPrincipal user, DemoStore store) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (store.Suppliers.All(s => s.Id != body.SupplierId)) errors["supplierId"] = ["Unknown supplier."];
                if (body.Amount <= 0) errors["amount"] = ["Amount must be greater than zero."];
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                var po = store.AddPurchaseOrder(body.SupplierId!, body.Amount, user.GetSub(), user.GetDisplayName());
                return Results.Created($"/purchase-orders/{po.Id}", po);
            })
            .RequireAuthorization(Policies.Procurement);

        orders.MapPost("/{id:guid}/approve", async (Guid id, ClaimsPrincipal user, DemoStore store, IAuthorizationService auth) =>
            {
                var po = store.FindPurchaseOrder(id);
                if (po is null) return Results.NotFound();

                var check = await auth.AuthorizeAsync(user, po, Policies.FinanceApprove);
                if (!check.Succeeded)
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Forbidden",
                        detail: "You can't approve a purchase order you raised.",
                        extensions: new Dictionary<string, object?> { ["policy"] = Policies.FinanceApprove });

                var approved = store.TryApprove(id, user.GetDisplayName());
                return approved is null
                    ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Already decided",
                        detail: "This purchase order has already been approved.")
                    : Results.Ok(approved);
            })
            .RequireAuthorization(Policies.FinanceRole);
    }
}
```

`src/Lantern.Api/Program.cs`: add after `app.MapHqEndpoints();`:
```csharp
app.MapPurchaseOrderEndpoints();
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~PurchaseOrderApprovalTests`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: purchase orders with finance approval and separation of duties"
```

---

### Task 7: Outlet-scoped endpoints

**Files:**
- Create: `src/Lantern.Api/Auth/OutletRequirement.cs`, `src/Lantern.Api/Endpoints/OutletEndpoints.cs`
- Modify: `src/Lantern.Api/Auth/Policies.cs`, `src/Lantern.Api/Program.cs`
- Test: `tests/Lantern.IntegrationTests/OutletScopeTests.cs`

**Interfaces:**
- Consumes: `Roles`, `Policies`, `DemoStore.Stock`/`Roster` (Task 5), and the `outlet_id` claim (Tasks 2–3).
- Produces:
  - `OutletRequirement(string Role)`. It passes when the user has `Role` and the route value `outletId` equals the token's `outlet_id` (case-insensitive), or when the user has `hq-admin`. Plan 2 reuses it as `new OutletRequirement(Roles.Cashier)` for `OutletSell`.
  - Routes `GET /outlets/{outletId}/stock` and `GET /outlets/{outletId}/roster`, both with policy `OutletManage`.

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/OutletScopeTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class OutletScopeTests(KeycloakFixture kc)
{
    [Theory]
    [InlineData("mgr.bangsar", "/outlets/BGS/stock", HttpStatusCode.OK)]
    [InlineData("mgr.bangsar", "/outlets/bgs/stock", HttpStatusCode.OK)]
    [InlineData("mgr.bangsar", "/outlets/KLC/stock", HttpStatusCode.Forbidden)]
    [InlineData("mgr.klcc", "/outlets/KLC/roster", HttpStatusCode.OK)]
    [InlineData("mgr.klcc", "/outlets/BGS/roster", HttpStatusCode.Forbidden)]
    [InlineData("aisha.admin", "/outlets/PJY/stock", HttpStatusCode.OK)]
    [InlineData("outlet-bangsar-1", "/outlets/BGS/roster", HttpStatusCode.Forbidden)]
    [InlineData("eric.procure", "/outlets/BGS/stock", HttpStatusCode.Forbidden)]
    [InlineData("mgr.bangsar", "/outlets/XXX/stock", HttpStatusCode.Forbidden)]
    [InlineData("aisha.admin", "/outlets/XXX/stock", HttpStatusCode.NotFound)]
    public async Task Outlet_routes_match_token_outlet(string user, string path, HttpStatusCode expected)
    {
        var response = await (await kc.Api.ClientAsAsync(user)).GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Lowercase_outlet_returns_that_outlets_data()
    {
        var client = await kc.Api.ClientAsAsync("mgr.bangsar");
        var stock = await client.GetFromJsonAsync<JsonElement>("/outlets/bgs/stock");
        Assert.Equal(42, stock.EnumerateArray().First().GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Forbidden_outlet_names_the_policy()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).GetAsync("/outlets/KLC/stock");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OutletManage", problem.GetProperty("policy").GetString());
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~OutletScopeTests`
Expected: FAIL (404 on `/outlets/...`).

- [ ] **Step 3: Implement**

`src/Lantern.Api/Auth/OutletRequirement.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Auth;

/// <summary>User must hold <see cref="Role"/> and belong to the outlet named in the route; hq-admin bypasses.</summary>
public sealed record OutletRequirement(string Role) : IAuthorizationRequirement;

public sealed class OutletRequirementHandler : AuthorizationHandler<OutletRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OutletRequirement requirement)
    {
        if (context.Resource is not HttpContext http) return Task.CompletedTask;
        if (http.GetRouteValue("outletId") is not string routeOutlet || routeOutlet.Length == 0) return Task.CompletedTask;

        if (context.User.IsInRole(Roles.HqAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userOutlet = context.User.FindFirstValue("outlet_id");
        if (context.User.IsInRole(requirement.Role) &&
            string.Equals(userOutlet, routeOutlet, StringComparison.OrdinalIgnoreCase))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

`src/Lantern.Api/Auth/Policies.cs`: add next to the other handler registration:
```csharp
        services.AddSingleton<IAuthorizationHandler, OutletRequirementHandler>();
```
and add this policy after the `FinanceApprove` line:
```csharp
            .AddPolicy(OutletManage, p => p.RequireAuthenticatedUser().AddRequirements(new OutletRequirement(Roles.OutletManager)))
```

`src/Lantern.Api/Endpoints/OutletEndpoints.cs`:
```csharp
using Lantern.Api.Auth;
using Lantern.Api.Data;

namespace Lantern.Api.Endpoints;

public static class OutletEndpoints
{
    public static void MapOutletEndpoints(this IEndpointRouteBuilder app)
    {
        var outlet = app.MapGroup("/outlets/{outletId}");

        outlet.MapGet("/stock", (string outletId, DemoStore store) =>
                store.Stock.TryGetValue(outletId, out var lines) ? Results.Ok(lines) : Results.NotFound())
            .RequireAuthorization(Policies.OutletManage);

        outlet.MapGet("/roster", (string outletId, DemoStore store) =>
                store.Roster.TryGetValue(outletId, out var roster) ? Results.Ok(roster) : Results.NotFound())
            .RequireAuthorization(Policies.OutletManage);
    }
}
```

`src/Lantern.Api/Program.cs`: add after `app.MapPurchaseOrderEndpoints();`:
```csharp
app.MapOutletEndpoints();
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~OutletScopeTests`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: outlet-scoped stock and roster endpoints"
```

---

### Task 8: Token introspection so deactivated users are rejected (scenario F)

**Files:**
- Create: `src/Lantern.Api/Auth/TokenIntrospector.cs`
- Modify: `src/Lantern.Api/Auth/AuthSetup.cs`
- Test: `tests/Lantern.IntegrationTests/TokenIntrospectorTests.cs` (no containers), `tests/Lantern.IntegrationTests/DeactivationTests.cs`

**Interfaces:**
- Consumes: `KeycloakOptions` (Task 4), and fixture helpers `CreateTempUserAsync`, `SetUserEnabledAsync`, `LogoutUserAsync`.
- Produces:
  - `enum IntrospectionResult { Active, Inactive, Unavailable }`.
  - `TokenIntrospector.CheckAsync(JsonWebToken token, CancellationToken ct)`. Active results are cached for `IntrospectionCacheSeconds` (not cached when it is 0). Inactive results are cached until the token's expiry. Unavailable is never cached.
  - JWT bearer `OnTokenValidated` fails authentication (401) for anything other than `Active`.

- [ ] **Step 1: Write the failing unit tests**

`tests/Lantern.IntegrationTests/TokenIntrospectorTests.cs`:
```csharp
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
```

- [ ] **Step 2: Write the failing integration tests**

`tests/Lantern.IntegrationTests/DeactivationTests.cs`:
```csharp
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
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --filter "FullyQualifiedName~TokenIntrospectorTests|FullyQualifiedName~DeactivationTests"`
Expected: FAIL. Compilation fails because `TokenIntrospector` and `IntrospectionResult` don't exist.

- [ ] **Step 4: Implement the introspector and hook it in**

`src/Lantern.Api/Auth/TokenIntrospector.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Lantern.Api.Auth;

public enum IntrospectionResult { Active, Inactive, Unavailable }

/// <summary>
/// Asks Keycloak whether a locally valid token is still active, so a deactivated user is locked out
/// within <see cref="KeycloakOptions.IntrospectionCacheSeconds"/> (spec §5.4).
/// </summary>
public sealed class TokenIntrospector(HttpClient http, IMemoryCache cache, IOptions<KeycloakOptions> options,
    ILogger<TokenIntrospector> logger)
{
    public async Task<IntrospectionResult> CheckAsync(JsonWebToken token, CancellationToken ct)
    {
        var kc = options.Value;
        var key = "introspect:" + (string.IsNullOrEmpty(token.Id)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.EncodedToken)))
            : token.Id);
        if (cache.TryGetValue(key, out IntrospectionResult cached)) return cached;

        IntrospectionResult result;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{kc.RealmUrl}/protocol/openid-connect/token/introspect")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = token.EncodedToken,
                    ["client_id"] = kc.ApiClientId,
                    ["client_secret"] = kc.ApiClientSecret
                })
            };
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Introspection returned {Status}", (int)response.StatusCode);
                return IntrospectionResult.Unavailable;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            result = doc.RootElement.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True
                ? IntrospectionResult.Active
                : IntrospectionResult.Inactive;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Introspection request failed");
            return IntrospectionResult.Unavailable;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Introspection request timed out");
            return IntrospectionResult.Unavailable;
        }

        if (result == IntrospectionResult.Inactive)
            cache.Set(key, result, token.ValidTo > DateTime.UtcNow
                ? new DateTimeOffset(token.ValidTo, TimeSpan.Zero)
                : DateTimeOffset.UtcNow.AddMinutes(1));
        else if (kc.IntrospectionCacheSeconds > 0)
            cache.Set(key, result, TimeSpan.FromSeconds(kc.IntrospectionCacheSeconds));

        return result;
    }
}
```

`src/Lantern.Api/Auth/AuthSetup.cs`: add `using Microsoft.IdentityModel.JsonWebTokens;` at the top. Then, after `services.Configure<KeycloakOptions>(…);`, add:
```csharp
        services.AddMemoryCache();
        services.AddHttpClient<TokenIntrospector>(c => c.Timeout = TimeSpan.FromSeconds(5));
```
and, as the last statement inside the `.Configure<IOptions<KeycloakOptions>>((o, kcOptions) => { … })` lambda, add:
```csharp
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var introspector = ctx.HttpContext.RequestServices.GetRequiredService<TokenIntrospector>();
                        var result = await introspector.CheckAsync((JsonWebToken)ctx.SecurityToken, ctx.HttpContext.RequestAborted);
                        if (result != IntrospectionResult.Active)
                            ctx.Fail($"Token rejected by introspection: {result}");
                    }
                };
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter "FullyQualifiedName~TokenIntrospectorTests|FullyQualifiedName~DeactivationTests"`
Expected: PASS (7 tests).
Then run: `dotnet test`
Expected: everything passes. Introspection is now on every authenticated request, and the earlier tests must still pass.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: reject deactivated users via keycloak introspection with short cache"
```

---

### Task 9: Staff management through the Keycloak Admin API (scenario I, staff part)

**Files:**
- Create: `src/Lantern.Api/Keycloak/KeycloakModels.cs`, `src/Lantern.Api/Keycloak/Departments.cs`, `src/Lantern.Api/Keycloak/KeycloakAdminClient.cs`, `src/Lantern.Api/Endpoints/StaffEndpoints.cs`
- Modify: `src/Lantern.Api/Program.cs`
- Test: `tests/Lantern.IntegrationTests/StaffManagementTests.cs`

**Interfaces:**
- Consumes: `KeycloakOptions`, `Policies.StaffAdmin`, `Roles.All`, `GetSub`, and the fixture's Mailpit helpers.
- Produces, extended in Plan 2 with cashier and till methods:
  - `KeycloakAdminClient` (typed HttpClient): `ListStaffAsync`, `GetUserAsync(id)` → `KcUser?`, `CreateStaffAsync(NewStaff)` → `string?` id (null on conflict), `SetDepartmentsAsync(id, departments)`, `DeactivateAsync(id)`, `ReactivateAsync(id)`, `SendActionsEmailAsync(id, actions)`.
  - `StaffMember(string Id, string Username, string Name, string? Email, bool Enabled, IReadOnlyList<string> Groups, IReadOnlyList<string> Roles)`.
  - `Departments.Paths`: `Admin`, `Marketing`, `Procurement`, `Finance` → `/HQ/...`.
  - Routes, all under policy `StaffAdmin`: `GET /staff`, `POST /staff`, `PUT /staff/{id}/departments`, `POST /staff/{id}/deactivate`, `POST /staff/{id}/reactivate`, `POST /staff/{id}/reset-password`.

- [ ] **Step 1: Write the failing tests**

`tests/Lantern.IntegrationTests/StaffManagementTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class StaffManagementTests(KeycloakFixture kc)
{
    private Task<HttpClient> Admin() => kc.Api.ClientAsAsync("aisha.admin");

    private async Task<JsonElement> FindStaffAsync(string username)
    {
        var all = await (await Admin()).GetFromJsonAsync<JsonElement>("/staff");
        return all.EnumerateArray().Single(s => s.GetProperty("username").GetString() == username);
    }

    private static string[] RolesOf(JsonElement staff) =>
        staff.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();

    private static object NewStaff(string username, params string[] departments) => new
    {
        username,
        firstName = "New",
        lastName = "Starter",
        email = $"{username}@lantern.test",
        departments
    };

    private static string NewUsername() => $"new.{Guid.NewGuid():N}"[..16];

    [Fact]
    public async Task Non_admin_cannot_list_staff()
    {
        var response = await (await kc.Api.ClientAsAsync("dina.marketing")).GetAsync("/staff");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Listing_shows_effective_roles_from_groups()
    {
        var eric = await FindStaffAsync("eric.procure");
        Assert.Equal(new[] { "hq-staff", "procurement" }, RolesOf(eric));
        Assert.Contains("/HQ/Procurement", eric.GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
    }

    [Fact]
    public async Task Creating_staff_assigns_department_roles_and_emails_an_invite()
    {
        var username = NewUsername();

        var response = await (await Admin()).PostAsJsonAsync("/staff", NewStaff(username, "Marketing"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new[] { "hq-staff", "marketing" }, RolesOf(await FindStaffAsync(username)));
        Assert.True(await kc.WaitForEmailCountAsync($"{username}@lantern.test", 1) >= 1);
    }

    [Fact]
    public async Task Unknown_department_is_400()
    {
        var response = await (await Admin()).PostAsJsonAsync("/staff", NewStaff(NewUsername(), "Kitchen"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_username_is_409()
    {
        var response = await (await Admin()).PostAsJsonAsync("/staff", NewStaff("eric.procure", "Marketing"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Changing_departments_replaces_roles()
    {
        var username = NewUsername();
        var created = await (await Admin()).PostAsJsonAsync("/staff", NewStaff(username, "Marketing"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await (await Admin()).PutAsJsonAsync($"/staff/{id}/departments", new { departments = new[] { "Finance" } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new[] { "finance", "hq-staff" }, RolesOf(await FindStaffAsync(username)));
    }

    [Fact]
    public async Task Empty_departments_is_400_and_leaves_memberships_alone()
    {
        var username = NewUsername();
        var created = await (await Admin()).PostAsJsonAsync("/staff", NewStaff(username, "Marketing"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await (await Admin()).PutAsJsonAsync($"/staff/{id}/departments", new { departments = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new[] { "hq-staff", "marketing" }, RolesOf(await FindStaffAsync(username)));
    }

    [Fact]
    public async Task Deactivate_locks_out_and_reactivate_restores()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        var userClient = kc.Api.ClientWithToken(await kc.GetFreshUserTokenAsync(username));
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await (await Admin()).PostAsync($"/staff/{id}/deactivate", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/me")).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => kc.GetFreshUserTokenAsync(username));

        Assert.Equal(HttpStatusCode.NoContent, (await (await Admin()).PostAsync($"/staff/{id}/reactivate", null)).StatusCode);
        var again = kc.Api.ClientWithToken(await kc.GetFreshUserTokenAsync(username));
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_self()
    {
        var admin = await Admin();
        var me = await admin.GetFromJsonAsync<JsonElement>("/me");

        var response = await admin.PostAsync($"/staff/{me.GetProperty("id").GetString()}/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await FindStaffAsync("aisha.admin")).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Reset_password_emails_the_user()
    {
        var (id, _, email) = await kc.CreateTempUserAsync("/HQ/Marketing");

        var response = await (await Admin()).PostAsync($"/staff/{id}/reset-password", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(await kc.WaitForEmailCountAsync(email, 1) >= 1);
    }

    [Fact]
    public async Task Unknown_user_is_404()
    {
        var response = await (await Admin()).PostAsync($"/staff/{Guid.NewGuid()}/deactivate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter FullyQualifiedName~StaffManagementTests`
Expected: FAIL (404 on `/staff`).

- [ ] **Step 3: Implement the Admin API client**

`src/Lantern.Api/Keycloak/KeycloakModels.cs`:
```csharp
namespace Lantern.Api.Keycloak;

public sealed record KcUser(string Id, string Username, string? FirstName, string? LastName, string? Email, bool Enabled);
public sealed record KcGroup(string Id, string Name, string Path);
public sealed record KcRole(string Name);

public sealed record StaffMember(string Id, string Username, string Name, string? Email, bool Enabled,
    IReadOnlyList<string> Groups, IReadOnlyList<string> Roles);

public sealed record NewStaff(string? Username, string? FirstName, string? LastName, string? Email, string[]? Departments);
public sealed record DepartmentsUpdate(string[]? Departments);
```

`src/Lantern.Api/Keycloak/Departments.cs`:
```csharp
namespace Lantern.Api.Keycloak;

/// <summary>HQ departments are Keycloak groups; the group grants the roles (spec §4.3).</summary>
public static class Departments
{
    public static readonly IReadOnlyDictionary<string, string> Paths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = "/HQ/Admin",
            ["Marketing"] = "/HQ/Marketing",
            ["Procurement"] = "/HQ/Procurement",
            ["Finance"] = "/HQ/Finance"
        };

    public static bool IsValid(string department) => Paths.ContainsKey(department);
    public static string ToGroupPath(string department) => Paths[department];
    public static bool IsDepartmentPath(string path) => Paths.Values.Contains(path);
}
```

`src/Lantern.Api/Keycloak/KeycloakAdminClient.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lantern.Api.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lantern.Api.Keycloak;

/// <summary>Keycloak Admin API calls made as the api-admin-svc service account (spec §5.5).</summary>
public sealed class KeycloakAdminClient(HttpClient http, IOptions<KeycloakOptions> options, IMemoryCache cache)
{
    private const string TokenCacheKey = "kc-admin-token";
    private KeycloakOptions Kc => options.Value;

    public async Task<IReadOnlyList<StaffMember>> ListStaffAsync(CancellationToken ct)
    {
        var users = await GetJsonAsync<List<KcUser>>("users?briefRepresentation=true&max=500", ct);
        var result = new List<StaffMember>(users.Count);
        foreach (var u in users)
        {
            var groups = await GetJsonAsync<List<KcGroup>>($"users/{u.Id}/groups", ct);
            var roles = await GetJsonAsync<List<KcRole>>($"users/{u.Id}/role-mappings/realm/composite", ct);
            result.Add(new StaffMember(
                u.Id, u.Username, $"{u.FirstName} {u.LastName}".Trim(), u.Email, u.Enabled,
                groups.Select(g => g.Path).Order().ToList(),
                roles.Select(r => r.Name).Where(Roles.All.Contains).Order().ToList()));
        }
        return result;
    }

    public async Task<KcUser?> GetUserAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"users/{Uri.EscapeDataString(id)}", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<KcUser>(ct);
    }

    /// <returns>The new user's id, or null when the username or email already exists.</returns>
    public async Task<string?> CreateStaffAsync(NewStaff staff, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "users", new
        {
            username = staff.Username,
            email = staff.Email,
            firstName = staff.FirstName,
            lastName = staff.LastName,
            enabled = true,
            emailVerified = true,
            groups = staff.Departments!.Select(Departments.ToGroupPath).ToArray()
        }, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return null;
        response.EnsureSuccessStatusCode();

        var id = response.Headers.Location!.Segments.Last();
        await SendActionsEmailAsync(id, ["UPDATE_PASSWORD"], ct);
        return id;
    }

    public async Task SetDepartmentsAsync(string id, IReadOnlyCollection<string> departments, CancellationToken ct)
    {
        var current = await GetJsonAsync<List<KcGroup>>($"users/{id}/groups", ct);
        var wanted = departments.Select(Departments.ToGroupPath).ToHashSet();

        foreach (var group in current.Where(g => Departments.IsDepartmentPath(g.Path) && !wanted.Contains(g.Path)))
            await SendOkAsync(HttpMethod.Delete, $"users/{id}/groups/{group.Id}", null, ct);

        foreach (var path in wanted.Where(p => current.All(g => g.Path != p)))
        {
            var group = await GetJsonAsync<KcGroup>($"group-by-path/{path.TrimStart('/')}", ct);
            await SendOkAsync(HttpMethod.Put, $"users/{id}/groups/{group.Id}", null, ct);
        }
    }

    /// <summary>Disable, end online sessions, and revoke till offline sessions (spec §5.4).</summary>
    public async Task DeactivateAsync(string id, CancellationToken ct)
    {
        await SetEnabledAsync(id, false, ct);
        await SendOkAsync(HttpMethod.Post, $"users/{id}/logout", null, ct);
        using var revoke = await SendAsync(HttpMethod.Delete, $"users/{id}/consents/till", null, ct);
        if (revoke.StatusCode != HttpStatusCode.NotFound) revoke.EnsureSuccessStatusCode();
    }

    public Task ReactivateAsync(string id, CancellationToken ct) => SetEnabledAsync(id, true, ct);

    public Task SendActionsEmailAsync(string id, string[] actions, CancellationToken ct) =>
        SendOkAsync(HttpMethod.Put, $"users/{id}/execute-actions-email", actions, ct);

    private async Task SetEnabledAsync(string id, bool enabled, CancellationToken ct)
    {
        // Send the full representation back: partial PUTs can trip user-profile validation.
        var user = await GetJsonAsync<JsonObject>($"users/{id}", ct);
        user["enabled"] = enabled;
        await SendOkAsync(HttpMethod.Put, $"users/{id}", user, ct);
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private async Task SendOkAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{Kc.AdminUrl}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetServiceTokenAsync(ct));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, ct);
    }

    private async Task<string> GetServiceTokenAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null) return cached;

        using var response = await http.PostAsync($"{Kc.RealmUrl}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = Kc.AdminClientId,
                ["client_secret"] = Kc.AdminClientSecret
            }), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var token = json.GetProperty("access_token").GetString()!;
        var lifetime = Math.Max(json.GetProperty("expires_in").GetInt32() - 30, 5);
        cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(lifetime));
        return token;
    }
}
```

- [ ] **Step 4: Implement the endpoints**

`src/Lantern.Api/Endpoints/StaffEndpoints.cs`:
```csharp
using System.Security.Claims;
using System.Text.RegularExpressions;
using Lantern.Api.Auth;
using Lantern.Api.Keycloak;

namespace Lantern.Api.Endpoints;

public static partial class StaffEndpoints
{
    [GeneratedRegex("^[a-z0-9.\\-]{3,40}$")]
    private static partial Regex UsernamePattern();

    public static void MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/staff").RequireAuthorization(Policies.StaffAdmin);

        staff.MapGet("/", async (KeycloakAdminClient kc, CancellationToken ct) =>
            Results.Ok(await kc.ListStaffAsync(ct)));

        staff.MapPost("/", async (NewStaff body, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            var errors = ValidateNewStaff(body);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var id = await kc.CreateStaffAsync(body, ct);
            return id is null
                ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Username or email already exists.")
                : Results.Created($"/staff/{id}", new { id });
        });

        staff.MapPut("/{id}/departments", async (string id, DepartmentsUpdate body, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            var errors = ValidateDepartments(body.Departments);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (await kc.GetUserAsync(id, ct) is null) return Results.NotFound();

            await kc.SetDepartmentsAsync(id, body.Departments!, ct);
            return Results.NoContent();
        });

        staff.MapPost("/{id}/deactivate", async (string id, ClaimsPrincipal user, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            if (id == user.GetSub())
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "You can't deactivate your own account.");
            if (await kc.GetUserAsync(id, ct) is null) return Results.NotFound();

            await kc.DeactivateAsync(id, ct);
            return Results.NoContent();
        });

        staff.MapPost("/{id}/reactivate", async (string id, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            if (await kc.GetUserAsync(id, ct) is null) return Results.NotFound();
            await kc.ReactivateAsync(id, ct);
            return Results.NoContent();
        });

        staff.MapPost("/{id}/reset-password", async (string id, KeycloakAdminClient kc, CancellationToken ct) =>
        {
            if (await kc.GetUserAsync(id, ct) is null) return Results.NotFound();
            await kc.SendActionsEmailAsync(id, ["UPDATE_PASSWORD"], ct);
            return Results.Accepted();
        });
    }

    private static Dictionary<string, string[]> ValidateNewStaff(NewStaff body)
    {
        var errors = ValidateDepartments(body.Departments);
        if (body.Username is null || !UsernamePattern().IsMatch(body.Username))
            errors["username"] = ["Use 3–40 lowercase letters, digits, dots or hyphens."];
        if (string.IsNullOrWhiteSpace(body.FirstName)) errors["firstName"] = ["First name is required."];
        if (string.IsNullOrWhiteSpace(body.LastName)) errors["lastName"] = ["Last name is required."];
        if (string.IsNullOrWhiteSpace(body.Email) || !body.Email.Contains('@')) errors["email"] = ["A valid email is required."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateDepartments(string[]? departments)
    {
        var errors = new Dictionary<string, string[]>();
        if (departments is null || departments.Length == 0)
            errors["departments"] = ["At least one department is required."];
        else if (departments.FirstOrDefault(d => !Departments.IsValid(d)) is { } bad)
            errors["departments"] = [$"Unknown department '{bad}'. Use one of: {string.Join(", ", Departments.Paths.Keys)}."];
        return errors;
    }
}
```

`src/Lantern.Api/Program.cs`: add after `builder.Services.AddSingleton<Lantern.Api.Data.DemoStore>();`:
```csharp
builder.Services.AddHttpClient<Lantern.Api.Keycloak.KeycloakAdminClient>();
```
and after `app.MapOutletEndpoints();`:
```csharp
app.MapStaffEndpoints();
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --filter FullyQualifiedName~StaffManagementTests`
Expected: PASS (11 tests). If a call returns 403 from Keycloak, the service account is missing a `realm-management` role. Add it to `service-account-api-admin-svc` in the realm file, then note it in the commit message.

Then run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: staff management through keycloak admin api"
```

---

### Task 10: API in Docker Compose, smoke check, and README stub

**Files:**
- Create: `src/Lantern.Api/Dockerfile`, `.dockerignore`, `scripts/smoke.sh`, `README.md`
- Modify: `docker-compose.yml`

**Interfaces:**
- Consumes: everything above.
- Produces: `docker compose up -d --build --wait` runs postgres, keycloak, mailpit and api. `scripts/smoke.sh` exits 0 when the stack is healthy.

- [ ] **Step 1: Write the failing smoke check**

`scripts/smoke.sh`:
```bash
#!/usr/bin/env bash
set -euo pipefail
fail() { echo "FAIL: $*" >&2; exit 1; }

"$(dirname "$0")/verify-hostname.sh"

[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5100/health)" == "200" ]] || fail "API /health not 200"
[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5100/me)" == "401" ]] || fail "API /me without token not 401"
[[ "$(curl -s -o /dev/null -w '%{http_code}' -H 'Authorization: Bearer nope' http://localhost:5100/me)" == "401" ]] || fail "API accepted a garbage token"
[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:8025/livez)" == "200" ]] || fail "Mailpit not up"

echo "OK: stack healthy"
```

```bash
chmod +x scripts/smoke.sh
./scripts/smoke.sh
```
Expected: `FAIL: API /health not 200` (the API isn't in compose yet).

- [ ] **Step 2: Containerise the API**

`.dockerignore`:
```
**/bin
**/obj
.git
keycloak/pin-authenticator/target
```

`src/Lantern.Api/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Lantern.Api/Lantern.Api.csproj src/Lantern.Api/
RUN dotnet restore src/Lantern.Api/Lantern.Api.csproj
COPY src/Lantern.Api/ src/Lantern.Api/
RUN dotnet publish src/Lantern.Api/Lantern.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Lantern.Api.dll"]
```

`docker-compose.yml`: add this service under `services:`, after `mailpit`:
```yaml
  api:
    build:
      context: .
      dockerfile: src/Lantern.Api/Dockerfile
    image: lantern-api:dev
    environment:
      Keycloak__BaseUrl: http://keycloak:8080
      Keycloak__Issuer: http://localhost:8080/realms/lantern
    ports:
      - "5100:8080"
    depends_on:
      keycloak:
        condition: service_healthy
```

- [ ] **Step 3: Run the smoke check and confirm it passes**

```bash
docker compose up -d --build --wait
./scripts/smoke.sh
```
Expected: `OK: issuer is …` followed by `OK: stack healthy`.

- [ ] **Step 4: Write the README stub**

`README.md`:
````markdown
# Lantern Auth

A learning and portfolio project: staff login for a fictional retail chain, **Lantern Mart**, built on
Keycloak. Three web apps and one API share one identity server. The hard case is a shared till: an
outlet account signs the till in once and stays signed in, then cashiers identify themselves with a PIN.

> Work in progress. Plan 1 of 4 (foundation: realm, API authorization, deactivation, staff
> management) is done. See `docs/superpowers/plans/2026-10-01-roadmap.md`.

## Run it

Requirements: Docker, and the .NET 10 SDK for running tests.

```bash
docker compose up -d --build --wait
./scripts/smoke.sh
```

| Service | URL |
|---|---|
| Keycloak (admin / admin) | http://localhost:8080 |
| API | http://localhost:5100/health |
| Mailpit (catches emails) | http://localhost:8025 |

Demo users and their roles are listed in the design spec, §4.7
(`docs/superpowers/specs/2026-10-01-lantern-auth-design.md`). Every password is `Lantern!2026`.

After changing `keycloak/realm/lantern-realm.json`, run `docker compose down -v` so the realm is
re-imported.

## Tests

```bash
dotnet test
```

The integration tests start their own Keycloak and Mailpit containers with Testcontainers. Docker must
be running.
````

- [ ] **Step 5: Run the whole suite one more time**

Run: `dotnet test`
Expected: all tests pass (about 71).

- [ ] **Step 6: Commit**

```bash
git add .dockerignore src/Lantern.Api/Dockerfile docker-compose.yml scripts/smoke.sh README.md
git commit -m "feat: run api in compose with smoke check and readme"
```
