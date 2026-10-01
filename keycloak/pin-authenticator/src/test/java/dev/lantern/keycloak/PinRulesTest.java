package dev.lantern.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.Optional;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.junit.jupiter.params.provider.ValueSource;

class PinRulesTest {

    @ParameterizedTest
    @ValueSource(strings = {"1111", "2580", "135790"})
    void fourToSixDigitsAreWellFormed(String pin) {
        assertTrue(PinRules.isWellFormed(pin));
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "123", "1234567", "12a4", " 1234", "12 34", "١٢٣٤"})
    void anythingElseIsNot(String pin) {
        assertFalse(PinRules.isWellFormed(pin));
    }

    @ParameterizedTest
    @CsvSource({
            "2580, 864213, ",
            "135790, 864213, ",
            "12a4, 864213, pin_rule_format",
            "864213, 864213, pin_rule_same_as_temporary",
            "0000, 864213, pin_rule_repeated_digit",
            "1234, 864213, pin_rule_sequence",
            "98765, 864213, pin_rule_sequence"
    })
    void newPinRules(String newPin, String temporaryPin, String expected) {
        assertEquals(Optional.ofNullable(expected), PinRules.checkNewPin(newPin, temporaryPin));
    }
}
