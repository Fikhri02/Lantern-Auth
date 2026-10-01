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

**Local only.** Everything runs over plain HTTP on `localhost`, and the realm sets
`sslRequired: none` so it also works behind VPNs that make local traffic look external. Don't expose
this stack to a network; production would need TLS, real secrets and a hardened realm.

## Tests

```bash
dotnet test
```

The integration tests start their own Keycloak and Mailpit containers with Testcontainers. Docker must
be running.
