# 0001. Tokens stay on the server (backend-for-frontend)

**Status:** Accepted

**Context.** Browser-held tokens can be stolen by any script that runs on the page.

**Decision.** All three web apps are confidential OIDC clients that keep tokens in a server-side session. The
browser holds only an HttpOnly cookie pointing at it. Sessions are indexed by Keycloak's `sid`, so back-channel
logout can find them.

**Consequences.** Tokens never reach the browser. Sessions live in memory, so restarting an app signs its users
out (a demo limit; production would use a shared store). The till's server is the only holder of its client
secret, which is what makes the PIN flow ([0002](0002-direct-grant-for-cashier-pin.md)) safe.
