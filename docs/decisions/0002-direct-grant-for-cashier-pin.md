# 0002. A locked-down direct grant for the cashier PIN

**Status:** Accepted

**Context.** Cashiers switch every few minutes. A full-page redirect per switch is slow at a till, and an
API-side PIN check would keep cashiers outside Keycloak.

**Options considered.** A PIN step on Keycloak's sign-in page (by the book, but a redirect per switch); a PIN
checked by the API (no Java, but cashiers outside Keycloak); a dedicated direct-grant flow (chosen).

**Decision.** The `till` client's only direct-grant flow is `till-cashier-pin`, built from three plugin steps. The
request must carry a live outlet login, the code must name a cashier of the same outlet, and the PIN must match.
It never accepts a password.

**Consequences.** OAuth 2.1 discourages the password grant; here it is confidential, server-only, single-client,
and useless without an outlet login. Cashiers are real Keycloak users, so deactivation, roles and audit work for
them.
