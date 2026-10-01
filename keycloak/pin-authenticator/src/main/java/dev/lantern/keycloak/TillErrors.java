package dev.lantern.keycloak;

/** error_description codes the till maps to messages (spec §7). */
public final class TillErrors {

    public static final String OUTLET_SESSION_INVALID = "outlet_session_invalid";
    public static final String CASHIER_NOT_FOUND = "cashier_not_found";
    public static final String CASHIER_WRONG_OUTLET = "cashier_wrong_outlet";
    public static final String CASHIER_DISABLED = "cashier_disabled";
    public static final String PIN_MISSING = "pin_missing";
    public static final String PIN_INVALID = "pin_invalid";
    public static final String PIN_LOCKED = "pin_locked";
    public static final String PIN_CHANGE_REQUIRED = "pin_change_required";

    private TillErrors() {
    }

    /** {@code pin_invalid:<remaining attempts before lockout>}, never negative. */
    public static String pinInvalid(int failureFactor, int failuresSoFar) {
        return PIN_INVALID + ":" + Math.max(failureFactor - failuresSoFar - 1, 0);
    }
}
