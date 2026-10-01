package dev.lantern.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

class TillErrorsTest {

    @ParameterizedTest
    @CsvSource({"5, 0, pin_invalid:4", "5, 3, pin_invalid:1", "5, 4, pin_invalid:0", "5, 9, pin_invalid:0"})
    void pinInvalidCarriesRemainingAttempts(int failureFactor, int failuresSoFar, String expected) {
        assertEquals(expected, TillErrors.pinInvalid(failureFactor, failuresSoFar));
    }
}
