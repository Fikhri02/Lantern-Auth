# 0004. Introspection with a short cache for deactivation

**Status:** Accepted

**Context.** Access tokens live 5 minutes. Deactivated staff shouldn't keep access for that long.

**Decision.** After local JWT validation, the API asks Keycloak whether the token is still active, caching
"active" for 15 seconds (configurable, 0 in tests) and "inactive" until the token expires. If Keycloak can't
answer, the request is refused.

**Consequences.** Lockout within the cache window, at the cost of one extra call per uncached request. Fails
closed during a Keycloak outage.
