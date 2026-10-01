# Lantern Auth

A learning and portfolio project: staff login for a fictional retail chain, **Lantern Mart**, built on
Keycloak. Three web apps and one API share one identity server. The hard case is a shared till: an
outlet account signs the till in once and stays signed in, then cashiers identify themselves with a PIN.

> Work in progress. Plans 1–4 of 5 are done: foundation, till backend, till app and web apps. Plan 5
> (browser tests, CI, scenario docs) is next. See `docs/superpowers/plans/2026-10-01-roadmap.md`.

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
| Till (sign in as `outlet-bangsar-2`, then PIN `c-1001` / `1111`) | http://localhost:5400 |
| Back Office (HQ staff, e.g. `dina.marketing`; admins set up an authenticator app) | http://localhost:5200 |
| Outlet Admin (managers, e.g. `mgr.bangsar`) | http://localhost:5300 |

Demo users and their roles are listed in the design spec, §4.7
(`docs/superpowers/specs/2026-10-01-lantern-auth-design.md`). Every password is `Lantern!2026`.

After changing `keycloak/realm/lantern-realm.json`, run `docker compose down -v` so the realm is
re-imported.

**Local only.** Everything runs over plain HTTP on `localhost`, and the realm sets
`sslRequired: none` so it also works behind VPNs that make local traffic look external. Don't expose
this stack to a network; production would need TLS, real secrets and a hardened realm.

## Try the till

1. Open http://localhost:5400 and click **Sign in this till**. Use `outlet-bangsar-2` / `Lantern!2026`.
   The till stays signed in from now on, even across restarts.
2. On the PIN pad, enter cashier `c-1001` and PIN `1111`. Ring a sale: the receipt names Siti and the till.
3. Try a second browser with the same till account: Keycloak refuses it, because one account means one till.
4. **Sign out this till** frees the account. An outlet manager can also release it with
   `DELETE /outlets/BGS/tills/{id}/session` on the API.

Cashier `c-3001` (PJ) has a temporary PIN `5555` and is asked to choose a new one.

## Try single sign-on and single logout

1. Sign in to Back Office as `aisha.admin`. Keycloak asks her to set up an authenticator app (MFA is for
   HQ admins only).
2. Open Outlet Admin in the same browser: no second sign-in.
3. Sign out of Back Office, then reload Outlet Admin: you're signed out there too (back-channel logout).
4. Try `mgr.bangsar` on Back Office: Keycloak refuses with "Your account doesn't have access to Back Office."
5. On Back Office → Promotions, a non-marketing user can press **Try it anyway** and see the API's 403.

## Tests

```bash
dotnet test
```

The integration tests start their own Keycloak and Mailpit containers with Testcontainers. Docker must
be running.
