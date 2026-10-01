# 0006. An offline token for the till's outlet login

**Status:** Accepted

**Context.** A till is signed in once and must stay signed in across browser, server and Keycloak restarts.

**Decision.** The till requests `offline_access`, stores the offline refresh token encrypted (ASP.NET Core Data
Protection) in SQLite on its volume, keyed by a random id in a long-lived HttpOnly cookie, and ends the online
session straight away. Imported outlet accounts get `offline_access` through the `outlet-device` role.

**Consequences.** Releasing or deactivating a till revokes the offline login. A Keycloak error other than a
rejected login never unregisters a till.
