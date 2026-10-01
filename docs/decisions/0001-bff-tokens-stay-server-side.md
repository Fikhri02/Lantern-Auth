# 0001. Tokens stay on the server (backend-for-frontend)

**Status:** Accepted

**Context.** Browser-held tokens can be stolen by any script that runs on the page.

**Decision.** All three web apps are confidential OIDC clients that keep tokens on their server; the browser holds
only an HttpOnly cookie. Back Office and Outlet Admin keep them in a server-side session indexed by Keycloak's
`sid`, so back-channel logout can find it. The till keeps its outlet login encrypted on its own volume instead
([0006](0006-offline-token-for-the-outlet-login.md)).

**Consequences.** Tokens never reach the browser. Back Office and Outlet Admin sessions live in memory, so
restarting either app signs its users out (a demo limit; production would use a shared store). The till's server is the only holder of its client
secret, which is what makes the PIN flow ([0002](0002-direct-grant-for-cashier-pin.md)) safe.
