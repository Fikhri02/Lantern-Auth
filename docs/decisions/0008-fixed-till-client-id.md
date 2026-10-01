# 0008. A fixed id for the till client instead of client-read rights

**Status:** Accepted

**Context.** Listing a till account's offline sessions needs the till client's internal id. Finding it at run
time would need `view-clients`, which also exposes client secrets.

**Decision.** The realm file gives the till client a fixed id, and the API reads it from configuration.

**Consequences.** The API's service account stays least-privileged. Re-creating the till client by hand would
change the id, and the setting would have to follow.
