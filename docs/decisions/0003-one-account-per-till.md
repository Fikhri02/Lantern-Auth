# 0003. One account per till, refuse rather than take over

**Status:** Accepted

**Context.** A till account must not be signed in on two tills. Keycloak's built-in session limiter only counts
online sessions, and the till's login is an offline one.

**Decision.** A plugin step refuses an outlet sign-in while the account has a live offline login for the till
client. It refuses rather than taking over, because silently taking over would strand a till mid-sale and let
anyone with the password move a till without a manager noticing. Managers release accounts from Outlet Admin.

**Consequences.** A lost or wiped till needs a manager's release. Adding a counter means adding an account.
