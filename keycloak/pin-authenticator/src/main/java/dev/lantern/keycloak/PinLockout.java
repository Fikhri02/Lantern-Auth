package dev.lantern.keycloak;

import java.util.Map;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;

/**
 * Wrong-PIN counter, kept apart from Keycloak's password lockout so that password guesses on other
 * login pages can't lock a cashier out of the till. Uses the realm's failure factor (5) and maximum
 * wait (15 min): the lock lifts that long after the last wrong PIN, and a right PIN clears the count.
 */
final class PinLockout {

    private static final String KEY_PREFIX = "lantern-pin-failures:";
    private static final String FAILURES = "failures";

    private PinLockout() {
    }

    static int failures(KeycloakSession session, UserModel user) {
        Map<String, String> notes = session.singleUseObjects().get(KEY_PREFIX + user.getId());
        return notes == null ? 0 : Integer.parseInt(notes.getOrDefault(FAILURES, "0"));
    }

    static boolean isLocked(KeycloakSession session, RealmModel realm, UserModel user) {
        return failures(session, user) >= realm.getFailureFactor();
    }

    /** @return the number of failures before this one */
    static int recordFailure(KeycloakSession session, RealmModel realm, UserModel user) {
        int before = failures(session, user);
        session.singleUseObjects().put(KEY_PREFIX + user.getId(), Math.max(realm.getMaxFailureWaitSeconds(), 60),
                Map.of(FAILURES, Integer.toString(before + 1)));
        return before;
    }

    static void clear(KeycloakSession session, UserModel user) {
        session.singleUseObjects().remove(KEY_PREFIX + user.getId());
    }
}
