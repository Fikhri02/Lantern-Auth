# Lantern Auth — Design Spec

**Date:** 2026-10-01
**Status:** Draft, awaiting review
**Author:** Irfan Fikhri

## 1. Purpose

A personal learning and portfolio project. It does two things:

1. **Rehearsal.** It builds a staff login system for a multi-app retail setup on Keycloak, including the
   hard case: a shared till where an outlet account signs in once, then cashiers switch with a PIN. The
   result answers, from experience, "can Keycloak handle this?"
2. **Showcase.** It lives in a public GitHub repo, with tests in CI, documentation for each scenario,
   and a three-command quickstart.

**The login system is the subject.** The business screens are stubs that exist only to show who
can do what.

**Fictional setting.** "Lantern Mart" is a small retail chain with an HQ and three outlets. Nothing is
taken from any employer's code, schema, endpoints, names or documents. Everything is written fresh.

### Success criteria

- `git clone` → `docker compose up` → open the README links, and all nine scenarios (§3) can be tried
  by hand using the demo users.
- Every scenario has at least one automated test, and CI runs all of them on every push.
- Every non-obvious choice has a short decision record that a reader can follow.

## 2. Architecture

```
            browser (cookies only, never tokens)
     ┌──────────────┬──────────────┬──────────────┐
     ▼              ▼              ▼              │
 BackOffice     OutletAdmin       Till            │  redirects for sign-in
 (Blazor)       (Blazor)        (Blazor)          ▼
     │              │              │ PIN    ┌───────────┐
     │  bearer      │  bearer      ├───────▶│ Keycloak  │◀── back-channel logout ──┐
     ▼              ▼              ▼        │ + plugin  │                          │
 ┌─────────────────────────────────────┐    └─────┬─────┘                          │
 │              Lantern.Api            │──────────┘ introspection, Admin API       │
 └─────────────────────────────────────┘                                           │
          ▲ each Blazor app also exposes /bff/backchannel-logout ──────────────────┘
```

### 2.1 Services (`docker-compose.yml`)

| Service | Role | Host port |
|---|---|---|
| `keycloak` | Identity server, current major release pinned to an exact tag. Imports the `lantern` realm on first boot. Image is built with the PIN plugin. | 8080 |
| `postgres` | Keycloak database | — |
| `api` | `Lantern.Api`, .NET 10 minimal API, stand-in for a back-end middleware | 5100 |
| `backoffice` | `Lantern.BackOffice`, Blazor Web App, HQ | 5200 |
| `outlet-admin` | `Lantern.OutletAdmin`, Blazor Web App, outlet managers | 5300 |
| `till` | `Lantern.Till`, Blazor Web App, outlet sign-in and then cashier PIN | 5400 |
| `mailpit` | Catches password-reset and staff-invite emails | 8025 |

### 2.2 Repository layout

```
lantern-auth/
  keycloak/
    Dockerfile                  # multi-stage: Maven/JDK 21 builds the plugin → copied into Keycloak image
    realm/lantern-realm.json    # clients, roles, groups, flows, demo users
    themes/lantern-hq/  lantern-outlet/  lantern-till/
    pin-authenticator/          # Java plugin (Maven project)
  src/
    Lantern.Auth/               # shared .NET library: OIDC/BFF setup, ticket store, back-channel logout
    Lantern.Api/
    Lantern.BackOffice/
    Lantern.OutletAdmin/
    Lantern.Till/
  tests/
    Lantern.IntegrationTests/   # xUnit + Testcontainers (Keycloak + API)
    Lantern.BrowserTests/       # Playwright for .NET
    (plugin unit tests live in keycloak/pin-authenticator/src/test)
  docs/
    scenarios/                  # one page per scenario A–I
    decisions/                  # short decision records
    superpowers/specs/          # this file
  .github/workflows/ci.yml
  docker-compose.yml
  README.md
```

### 2.3 Guiding principle: tokens never reach the browser

All three Blazor apps are **confidential OIDC clients running on the server**, using the backend-for-
frontend (BFF) pattern. The browser holds only an encrypted cookie that references a **server-side
session** (an `ITicketStore`). Access and refresh tokens are stored in that session. This is what makes
the till's PIN flow safe: only the till's server holds the client secret that the PIN flow requires.

### 2.4 Toolchain

- .NET 10 SDK, which needs installing; this machine currently has only .NET 6 SDKs.
- Docker. The plugin is built inside Docker, so no local JDK or Maven is needed. A local JDK 21 is
  optional, for running plugin tests from the IDE.

## 3. Scenarios

| ID | Scenario | Mechanism |
|---|---|---|
| A | Single sign-on across Back Office and Outlet Admin | Keycloak SSO session cookie |
| B | Single logout | OIDC back-channel logout to each app |
| C | Roles enforced in the API (role, outlet scope, separation of duties) | API authorization policies |
| D | Two-layer till login: outlet account, then cashier PIN | Custom Keycloak plugin, direct-grant flow |
| E | One active session per outlet account | Built-in User Session Count Limiter |
| F | Deactivation locks a user out | Disable + logout via Admin API; API introspection |
| G | MFA (authenticator app) for HQ admins only | Conditional OTP in browser flow |
| H | Branded login page per app | Per-client login theme |
| I | Staff management from Back Office | API → Keycloak Admin API via service account |

## 4. Identity model

### 4.1 Realm

The single realm `lantern` is fully defined in `keycloak/realm/lantern-realm.json` and imported on
first boot. Changes made in the Keycloak admin console are exported back to that file
(`kc.sh export`), so the repo stays the source of truth.

### 4.2 Roles (realm roles)

| Role | Signs in to | Grants (API) |
|---|---|---|
| `hq-admin` | Back Office, Outlet Admin | `/staff/*` (I), all read endpoints; MFA required |
| `hq-staff` | Back Office | Dashboard, read-only reports. Base role for every HQ user |
| `marketing` | Back Office | `/promotions` create, schedule, edit |
| `procurement` | Back Office | `/suppliers`, `/purchase-orders` create, `/stock` read |
| `finance` | Back Office | `/reports/sales`, `/purchase-orders/{id}/approve` |
| `outlet-manager` | Outlet Admin | Own outlet's `/stock`, `/roster` and `/tills` (close a stuck till); `/promotions` read only |
| `outlet-device` | Till (layer 1) | Opens the till for its outlet; cannot ring sales itself |
| `cashier` | Till (layer 2, PIN only) | `/sales` for its outlet |

### 4.3 Groups

```
HQ
  HQ/Admin        → hq-admin, hq-staff
  HQ/Marketing    → hq-staff, marketing
  HQ/Procurement  → hq-staff, procurement
  HQ/Finance      → hq-staff, finance
Outlets
  Outlets/Bangsar   (attribute outlet_id=BGS)
  Outlets/KLCC      (attribute outlet_id=KLC)
  Outlets/PJ        (attribute outlet_id=PJY)
```

Roles are attached to **groups**, not to users directly, except `outlet-manager`, `outlet-device` and
`cashier`, which are assigned per user, because outlet groups represent a place, not a job.

Each outlet group has an `outlet_id` attribute. Keycloak has no built-in mapper that copies a
**group attribute** into a token, so the plugin includes a small `outlet-id` protocol mapper. It
emits `outlet_id` from the user's outlet group. An HQ user has no `outlet_id`.

### 4.4 Token claims

Every client's access token carries:

- `aud` includes `lantern-api` (audience mapper on each client)
- `roles`: flat array of realm roles (realm-role mapper, claim name `roles`)
- `outlet_id`: for outlet users only (custom mapper)
- `sid`: Keycloak session id (standard)
- Cashier tokens also carry `outlet_session`: the outlet device's session id (§6)

### 4.5 Clients

| Client | Type | Flows enabled | Notes |
|---|---|---|---|
| `backoffice` | Confidential | Standard (code + PKCE) | Browser-flow override `browser-hq`: deny unless `hq-staff` or `hq-admin`; conditional OTP for `hq-admin`. Back-channel logout URL set. Login theme `lantern-hq`. |
| `outlet-admin` | Confidential | Standard | Browser-flow override `browser-outlet`: deny unless `outlet-manager` or `hq-admin`; same conditional OTP. Login theme `lantern-outlet`. |
| `till` | Confidential | Standard **and** Direct access grants | Browser-flow override `browser-till`: deny unless `outlet-device`; session limiter (E). Direct-grant override `till-cashier-pin` (§6). Login theme `lantern-till`. |
| `lantern-api` | Confidential | None | The audience name, plus credentials used **only** for token introspection |
| `api-admin-svc` | Confidential | Service account only | Service-account roles: `realm-management` → `manage-users`, `view-users`, `query-groups`. Used only by the API (§5.5). |

"Deny unless role X" uses Keycloak's built-in `Condition - user role` and `Deny access` steps in a
conditional sub-flow, so an unauthorised user is stopped **at Keycloak** with a themed error page,
before any app session exists.

### 4.6 Token and session lifetimes (proposals, tune during build)

| Setting | Value |
|---|---|
| Access token | 5 min (apps refresh server-side) |
| SSO session idle / max | 30 min / 10 h |
| `till` client session idle / max | 12 h / 14 h (one shift), client-level override. Applies to outlet **and** cashier sessions, because both belong to the `till` client |
| Cashier idle | 10 min, enforced by the till's server (§6.5), not by Keycloak |
| Introspection cache in API | 15 s (configurable, 0 for demos) |

### 4.7 Demo users

Every password is `Lantern!2026`. Every PIN is shown below. The README repeats this table.

| Username | Groups / roles | Notes |
|---|---|---|
| `aisha.admin` | HQ/Admin | MFA set up on first sign-in |
| `ben.admin` | HQ/Admin | Second admin, for the "deactivate another admin" demo |
| `chloe.staff` | HQ (hq-staff only) | Read-only HQ |
| `dina.marketing` | HQ/Marketing | |
| `eric.procure` | HQ/Procurement | Raises POs |
| `farah.finance` | HQ/Finance | Approves POs |
| `gary.multi` | HQ/Marketing + HQ/Procurement | Shows combined access |
| `hana.dual` | HQ/Procurement + HQ/Finance | Shows separation of duties: can approve others' POs, never her own |
| `mgr.bangsar`, `mgr.klcc`, `mgr.pj` | Outlets/… + `outlet-manager` | One per outlet |
| `outlet-bangsar`, `outlet-klcc`, `outlet-pj` | Outlets/… + `outlet-device` | Till accounts |
| `c-1001`, `c-1002` | Outlets/Bangsar + `cashier` | PINs `1111`, `2222` |
| `c-2001`, `c-2002` | Outlets/KLCC + `cashier` | PINs `3333`, `4444` |
| `c-3001` | Outlets/PJ + `cashier` | PIN `5555`; seeded as **temporary**, to demo first-use PIN change |

Cashiers have **no password credential**, only a PIN. They cannot use any browser sign-in.

## 5. Web sign-in, logout and access control

### 5.1 Sign-in and SSO (A)

- Each Blazor app calls a single `Lantern.Auth` extension, `AddLanternBff(config)`, which wires up
  cookie + OpenID Connect (code flow + PKCE, `SaveTokens`, server-side `ITicketStore`).
- **Cookie names differ per app** (`lantern.bo`, `lantern.oa`, `lantern.till`). Browsers don't separate
  cookies by port, so on `localhost` the apps would otherwise overwrite each other's cookies.
- Token refresh happens in the cookie's `OnValidatePrincipal`. When the refresh token is rejected, the
  session is dropped and the user is sent to sign in again.
- SSO works because Keycloak's own session cookie (on `localhost:8080`) is shared across clients.

### 5.2 Single logout (B)

1. A user clicks Sign out in any app. The app deletes its own session and redirects to Keycloak's
   end-session endpoint.
2. Keycloak ends its session and POSTs a signed `logout_token` to every client's back-channel logout
   URL that took part in the session.
3. `Lantern.Auth` maps `POST /bff/backchannel-logout` in each app. It validates the token (signature,
   `iss`, `aud`, `events` claim, no `nonce`), then deletes every ticket indexed under that `sid`.
4. Blazor Server keeps an open circuit until it re-checks auth, so each app uses a
   `RevalidatingServerAuthenticationStateProvider` with a **30-second** interval that checks the ticket
   still exists. An open tab therefore shows the sign-in page on its next navigation, or within 30 s.

The ticket store is **in-memory**, so restarting an app signs its users out. This is documented as a
demo limit.

### 5.3 API authorization (C)

`Lantern.Api` validates JWTs locally (issuer, audience `lantern-api`, signature via JWKS) and then
applies named policies:

| Policy | Rule |
|---|---|
| `HqRead` | `hq-staff` or `hq-admin` |
| `Marketing` | `marketing` |
| `Procurement` | `procurement` |
| `FinanceApprove` | `finance` **and** caller `sub` ≠ PO creator `sub` (resource-based handler) |
| `OutletScoped` | `outlet-manager`, or `cashier` / `outlet-device`, and the route's outlet equals the token's `outlet_id`; `hq-admin` bypasses |
| `StaffAdmin` | `hq-admin` |

Business data (promotions, POs, stock, sales) is held in memory and seeded on start. Each stub
endpoint returns enough to make the authorization result visible: a list, a 403 with a reason, or a
created record showing who created it.

Each app hides the menu items a user cannot use, but **the API is the enforcement point**. Scenario
C's demo includes a "try anyway" button that calls a forbidden endpoint directly and shows the 403.

### 5.4 Deactivation (F)

1. An admin clicks Deactivate in Back Office, which calls `POST /staff/{id}/deactivate`.
2. The API, using `api-admin-svc`, sets `enabled=false` and then calls `POST
   /admin/realms/lantern/users/{id}/logout` to end every session.
3. Any refresh attempt now fails, so each app drops that user's session on the next refresh.
4. To reject **already-issued** access tokens, the API runs a second check after JWT validation:
   token introspection using `lantern-api`'s credentials, with results cached by `jti` for
   `Introspection:CacheSeconds` (default 15). Inactive tokens get 401.

The claim in the docs is "locked out within the cache window", which is 15 s by default and 0 in demo
mode. Back-channel logout (B) also fires on step 2, so the user's open tabs sign out too.

### 5.5 Staff management (I)

Back Office never calls Keycloak's Admin API directly. It calls `Lantern.Api`, which checks
`StaffAdmin` and then calls Keycloak using `api-admin-svc` (client-credentials token, cached until
expiry).

| Endpoint | Keycloak action |
|---|---|
| `GET /staff` | List users with groups and roles |
| `POST /staff` | Create user, add to department group, send "set password" email (`execute-actions-email` → Mailpit) |
| `PUT /staff/{id}/groups` | Replace department group membership |
| `POST /staff/{id}/deactivate` / `reactivate` | §5.4 |
| `POST /staff/{id}/reset-password` | `execute-actions-email` with `UPDATE_PASSWORD` |
| `POST /cashiers` | Create cashier in an outlet group with a **temporary** PIN |
| `POST /cashiers/{id}/reset-pin` | Set a temporary PIN via the plugin's admin endpoint (§6.6) |
| `GET /outlets/{id}/tills` / `DELETE /outlets/{id}/tills/{sid}` | List the outlet account's `till` sessions; end one (`DELETE /admin/realms/lantern/sessions/{sid}`). Policy `OutletScoped` for `outlet-manager`, or `hq-admin` (§6.1) |

### 5.6 MFA (G)

In `browser-hq` and `browser-outlet`, a conditional sub-flow (`Condition - user role: hq-admin` →
`OTP Form`) runs after the password step. An `hq-admin` without OTP set up gets the built-in
`CONFIGURE_TOTP` required action on first sign-in (QR code). No other role sees MFA.

### 5.7 Themes (H)

`lantern-hq`, `lantern-outlet` and `lantern-till` each extend Keycloak's default login theme
(`parent=keycloak.v2`) and override only CSS, the logo and the `login.ftl` header. `lantern-till` uses
large touch targets for a kiosk look. Each client sets its own login theme. The "deny access" error
page is themed too.

## 6. Till: two-layer login (D, E)

### 6.1 Layer 1: outlet sign-in

- The till redirects to Keycloak (`lantern-till` theme). The outlet account signs in with its password.
- The `browser-till` flow includes the built-in **User Session Count Limiter**: maximum 1 session per
  user for this client, with the behaviour **Deny new session** by default. The decision record
  explains why refusing is safer than ending the oldest session, which would kick a till mid-sale. The
  demo switches the setting to show both behaviours.
- **Stuck-till recovery.** With "deny new session", a till that crashes or is closed without signing
  out would keep its session alive until the 12 h idle timeout and lock its outlet out. Outlet Admin
  therefore has an **Open tills** page. It lists the outlet account's live `till` session (device
  start time, last activity, IP) and has a **Close till** button that ends it through the API (§5.5).
  The session limiter's denial page tells the user to ask their outlet manager.
- After sign-in, the till's server session holds the outlet's tokens, and the UI shows the PIN pad.

### 6.2 Layer 2: cashier PIN

1. The cashier enters a code and PIN, which are posted to the till's own server.
2. The till's server calls Keycloak's token endpoint:
   ```
   POST /realms/lantern/protocol/openid-connect/token
   grant_type=password
   client_id=till  client_secret=…
   username=c-1001
   pin=1111
   outlet_token=<outlet device's current access token>
   [new_pin=…]          # only when changing a temporary PIN (§6.4)
   ```
3. Keycloak runs the `till-cashier-pin` direct-grant flow. Its steps, in order, are all REQUIRED and
   all live in the plugin:

| Step | Checks | Error code returned to the till |
|---|---|---|
| `outlet-session-check` | `outlet_token` signature, expiry and issuer are valid; holder has `outlet-device`; its `sid` session **still exists** in Keycloak | `outlet_session_invalid` |
| `cashier-check` | `username` exists, is enabled, has `cashier`, and is in the **same outlet group** as the device | `cashier_not_found` / `cashier_wrong_outlet` / `cashier_disabled` |
| `pin-check` | `pin` matches the user's `lantern-pin` credential; brute-force state respected | `pin_invalid` (with remaining tries) / `pin_locked` / `pin_change_required` |

4. On success, the plugin sets a user-session note `outlet_session=<device sid>`, and a session-note
   mapper adds it to the cashier's token as `outlet_session`.
5. The till's server stores the cashier's tokens in its session, alongside the outlet's tokens, and
   ends the previous cashier's Keycloak session (OIDC logout with that cashier's refresh token).
6. Sales calls to the API use the **cashier's** token. The API records `sub` (cashier) and
   `outlet_session` (device) on each sale.

### 6.3 Brute-force protection

The realm enables brute-force detection (5 failures → temporary lock, increasing wait). `pin-check`
reports failures as `invalid_user_credentials` login errors against the cashier, so Keycloak's
brute-force protector counts them, and it checks the protector before validating. **Implementation
risk:** custom direct-grant authenticators must report failures exactly the way the built-in password
step does, or they won't count. This is verified by an integration test early in the build.

### 6.4 Temporary PINs and first-use change

Keycloak's direct-grant flow **cannot run required actions**, so the browser-style "set your PIN"
step is not available. Instead:

- A `lantern-pin` credential can be marked **temporary** (stored in its credential data).
- If `pin-check` matches a temporary PIN and no `new_pin` was sent, it fails with
  `pin_change_required`. The till then shows a "choose a new PIN" screen.
- The till resubmits with both `pin` (the temporary one) and `new_pin`. `pin-check` validates the
  temporary PIN, checks the new PIN's rules (4–6 digits, not the same as the temporary one, not all
  one digit, not a simple run like `1234`), replaces the credential as non-temporary, and lets the
  flow succeed.

### 6.5 Idle lock

The till tracks the last activity time per cashier in its server session. After 10 minutes idle, it
ends the cashier's Keycloak session and shows the PIN pad. The outlet session continues. Keycloak
does not enforce the 10 minutes, because the `till` client's idle setting must fit the 12 h outlet
session. If the till's server dies, the cashier tokens die with its in-memory session, and the
leftover cashier sessions expire on the client idle timeout. They are unusable meanwhile, because
using them requires the client secret and a live outlet session.

### 6.6 The plugin (`keycloak/pin-authenticator`)

A Maven project (Java 21) built against the Keycloak SPI version matching the pinned image.

| Component | SPI | Purpose |
|---|---|---|
| `PinCredentialProvider` | `CredentialProvider` | Credential type `lantern-pin`. Hashes the PIN using Keycloak's configured `PasswordHashProvider`. Stores the `temporary` flag. |
| `OutletSessionCheckAuthenticator` | `Authenticator` | §6.2 step 1 |
| `CashierCheckAuthenticator` | `Authenticator` | §6.2 step 2 |
| `PinCheckAuthenticator` | `Authenticator` | §6.2 step 3, §6.3, §6.4 |
| `OutletIdMapper` | `ProtocolMapper` | §4.3: group attribute → `outlet_id` claim |
| `PinAdminResource` | `RealmResourceProvider` | `PUT /realms/lantern/lantern-pin/users/{id}`: set a temporary PIN. Requires a bearer token with `manage-users`; called only by the API. |

The `Dockerfile` builds the plugin in a `maven:3-eclipse-temurin-21` stage, copies the jar into
`/opt/keycloak/providers/`, and runs `kc.sh build`.

### 6.7 Why the direct-grant flow is acceptable here

OAuth 2.1 drops the password grant, and Keycloak discourages it in general. Here it is restricted:

- it is enabled only on the `till` client, which is confidential, and the secret exists only on the
  till's server
- the client's only direct-grant flow is `till-cashier-pin`, so there is no generic username/password
  path
- every request must carry a live outlet-device session from a normal browser sign-in
- it never handles a password, only a PIN that is useless without that outlet session

`docs/decisions/0002-direct-grant-for-cashier-pin.md` records this, along with the alternatives
considered: a redirect-based PIN step, and an API-side PIN check.

## 7. User-facing errors

Every app maps errors to plain messages. A raw Keycloak or HTTP error never reaches the user.

| Situation | Message |
|---|---|
| `pin_invalid` | "Wrong PIN. N tries left." |
| `pin_locked` | "Too many wrong PINs. Try again in M minutes or ask a manager." |
| `pin_change_required` | Opens the new-PIN screen |
| `cashier_wrong_outlet` | "This cashier isn't assigned to this outlet." |
| `outlet_session_invalid` | Till returns to outlet sign-in: "This till has been signed out." |
| Session limiter denial | Themed Keycloak page: "This outlet is already open on another device. Ask your outlet manager to close it." |
| Deny-access step | Themed Keycloak page: "Your account doesn't have access to {app}." |
| Deactivated user | "Your account has been deactivated." |
| API 403 | "You don't have permission to {action}." plus the policy name, for the demo |
| Keycloak unreachable | "Sign-in is temporarily unavailable." |

## 8. Local hostnames

The browser reaches Keycloak at `http://localhost:8080`. Containers reach it at `http://keycloak:8080`.
To keep one token issuer for both, Keycloak runs with `KC_HOSTNAME=http://localhost:8080` and
`KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`. Apps set `Authority` to `http://localhost:8080/realms/lantern`
for issuer validation, and `MetadataAddress` / back-channel calls to `http://keycloak:8080/…`.
**Implementation risk:** this is the usual stumbling block with Keycloak in Docker. It's verified
first, before any app code.

Back-channel logout URLs in the realm use the compose service names (`http://backoffice:8080/…`). For
running apps on the host with `dotnet run`, a documented `.env` override switches them to
`http://host.docker.internal:52xx/…`.

## 9. Testing

| Layer | Tool | Covers |
|---|---|---|
| Plugin unit | JUnit 5 + Mockito | PIN hashing and verification, temporary PIN rules, outlet match, dead outlet session, error codes |
| Integration | xUnit + Testcontainers (`Testcontainers.Keycloak`, image built from `keycloak/Dockerfile`) + `WebApplicationFactory<Lantern.Api>` | C: every policy, including finance self-approval denial. E: "Close till" frees the slot. D: full PIN flow and each error code. D: brute-force lock counts PIN failures. D: temporary PIN change. E: second outlet session denied. F: deactivated user's token rejected within the window. I: create staff, change groups, reset PIN; email visible in Mailpit. |
| Browser | Playwright for .NET, against `docker compose` | A: SSO across two apps. B: logout in one signs out the other (within 30 s). G: MFA with a TOTP generated in the test (Otp.NET). H: each client shows its theme. D: till UI happy path. |

The README has a scenario → test table linking each scenario to its tests.

**CI** (`.github/workflows/ci.yml`): build plugin and run its unit tests → build .NET solution → run
integration tests → `docker compose up` → run browser tests → upload the Playwright trace if they
fail. The CI badge goes in the README.

## 10. Documentation

- **README.md**, written for someone viewing the repo: a one-paragraph pitch, the architecture
  diagram, quickstart (`git clone`, `docker compose up`, open links), the demo users table, the
  scenario table with "try it" and test links, the demo limits (§11), and toolchain notes.
- **`docs/scenarios/a-sso.md` … `i-staff-management.md`**: a Mermaid sequence diagram, what to
  click, what you should see, and how it works.
- **`docs/decisions/`**:
  - 0001: BFF, tokens stay server-side
  - 0002: direct grant for cashier PIN
  - 0003: deny new session rather than end the oldest
  - 0004: introspection with a short cache for deactivation
  - 0005: group attribute mapper for `outlet_id`

## 11. Out of scope

- Production hardening: TLS, high availability, Redis or distributed ticket store, secrets management
- Real retail features beyond the stub endpoints in §5.3
- Mobile or native clients, social login, LDAP or AD federation, SSO with an external identity provider
- Anything taken from any employer's systems

## 12. Implementation risks to verify first

1. Hostname setup (§8): one issuer for the browser and the containers.
2. Brute-force counting from a custom direct-grant authenticator (§6.3).
3. Session limiter counting only the outlet account's `till` sessions, and "Close till" freeing the
   slot immediately (§6.1).
4. Blazor Server circuit revalidation picking up back-channel logout (§5.2).

Each one gets a small spike at the start of the implementation plan, before the code that depends on
it.
