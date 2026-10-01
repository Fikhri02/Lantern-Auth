package dev.lantern.keycloak;

import java.util.Optional;
import java.util.regex.Pattern;

/** Rules for PINs a cashier chooses (spec §6.4). Pure functions so they can be unit-tested. */
public final class PinRules {

    private static final Pattern WELL_FORMED = Pattern.compile("[0-9]{4,6}");

    private PinRules() {
    }

    public static boolean isWellFormed(String pin) {
        return pin != null && WELL_FORMED.matcher(pin).matches();
    }

    /** @return the error code when {@code newPin} is not acceptable, empty when it is. */
    public static Optional<String> checkNewPin(String newPin, String temporaryPin) {
        if (!isWellFormed(newPin)) return Optional.of("pin_rule_format");
        if (newPin.equals(temporaryPin)) return Optional.of("pin_rule_same_as_temporary");
        if (newPin.chars().distinct().count() == 1) return Optional.of("pin_rule_repeated_digit");
        if (isSimpleRun(newPin)) return Optional.of("pin_rule_sequence");
        return Optional.empty();
    }

    private static boolean isSimpleRun(String pin) {
        boolean up = true;
        boolean down = true;
        for (int i = 1; i < pin.length(); i++) {
            int step = pin.charAt(i) - pin.charAt(i - 1);
            up &= step == 1;
            down &= step == -1;
        }
        return up || down;
    }
}
