# 0005. A small mapper copies the outlet from the group into tokens

**Status:** Accepted

**Context.** Outlets are Keycloak groups with an `outlet_id` attribute. Keycloak has no built-in mapper for group
attributes, and copying the value onto each user would let the two drift apart.

**Decision.** The plugin includes `lantern-outlet-id-mapper`, which emits `outlet_id` from the user's outlet
group.

**Consequences.** Moving someone between outlets is one group change. A user in two outlet groups would get the
first one found; the data model doesn't allow that.
