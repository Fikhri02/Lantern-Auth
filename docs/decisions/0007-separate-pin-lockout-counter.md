# 0007. Wrong PINs have their own lockout counter

**Status:** Accepted (revised during Plan 2's review)

**Context.** Keycloak's brute-force counter is per user and shared by every login page. With it, anyone could
lock a cashier out of the till by typing wrong passwords for their code on any Keycloak page, and a right PIN
didn't reset the count.

**Decision.** The PIN step keeps its own counter in Keycloak's single-use store, using the realm's settings: five
wrong PINs lock for 15 minutes after the last one, a right PIN clears it, and an admin PIN reset lifts it.

**Consequences.** PIN lockout is independent of password lockout.
