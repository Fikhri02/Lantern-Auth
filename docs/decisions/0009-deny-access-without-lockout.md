# 0009. Turning someone away from an app doesn't count toward lockout

**Status:** Accepted (found by the browser tests in Plan 5)

**Context.** Each app's browser flow refuses users without the app's role, after they've signed in correctly.
Keycloak's built-in `deny-access-authenticator` reports that refusal as a failed login, so brute-force detection
counted it: opening the wrong app five times locked the account out of the apps it is allowed into, for up to
15 minutes.

**Decision.** The plugin's `lantern-deny-access` step shows the same themed error page (with the same
`denyErrorMessage` setting) as a challenge rather than a failure, as the one-till-per-account step already did. It
still records an `access_denied` login event.

**Consequences.** Only wrong passwords and codes count toward lockout. One more plugin class to maintain.
