package dev.lantern.keycloak;

import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;
import java.util.LinkedList;
import java.util.List;
import java.util.Optional;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.directgrant.AbstractDirectGrantAuthenticator;
import org.keycloak.authentication.authenticators.util.AuthenticatorUtils;
import org.keycloak.events.Errors;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserLoginFailureModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.credential.PasswordCredentialModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Step 3 of till-cashier-pin: checks the PIN, respects brute-force lockout, and replaces a
 * temporary PIN in the same request (spec §6.2–6.4).
 *
 * Wrong PINs call failure() with the user set, so Keycloak's brute-force protector counts them.
 * Everything else (missing PIN, locked, change required, rule broken) uses challenge(), which returns
 * the same JSON error without counting.
 */
public class PinCheckAuthenticator extends AbstractDirectGrantAuthenticator {

    public static final String PROVIDER_ID = "lantern-pin-check";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        UserModel cashier = context.getUser();
        MultivaluedMap<String, String> form = context.getHttpRequest().getDecodedFormParameters();
        String pin = form.getFirst("pin");
        String newPin = form.getFirst("new_pin");

        if (pin == null || pin.isBlank()) {
            reject(context, TillErrors.PIN_MISSING);
            return;
        }
        if (AuthenticatorUtils.getDisabledByBruteForceEventError(context, cashier) != null) {
            context.getEvent().user(cashier).error(Errors.USER_TEMPORARILY_DISABLED);
            reject(context, TillErrors.PIN_LOCKED);
            return;
        }

        switch (PinCredentials.check(context.getSession(), cashier, pin)) {
            case NO_PIN, MISMATCH -> {
                int failuresSoFar = failuresSoFar(context, cashier);
                context.getEvent().user(cashier).error(Errors.INVALID_USER_CREDENTIALS);
                context.failure(AuthenticationFlowError.INVALID_CREDENTIALS, errorResponse(
                        Response.Status.BAD_REQUEST.getStatusCode(), "invalid_grant",
                        TillErrors.pinInvalid(context.getRealm().getFailureFactor(), failuresSoFar)));
            }
            case OK_TEMPORARY -> {
                if (newPin == null) {
                    reject(context, TillErrors.PIN_CHANGE_REQUIRED);
                    return;
                }
                Optional<String> problem = PinRules.checkNewPin(newPin, pin);
                if (problem.isPresent()) {
                    reject(context, problem.get());
                    return;
                }
                PinCredentials.set(context.getSession(), cashier, newPin, false);
                context.success();
            }
            case OK -> context.success();
        }
    }

    private static int failuresSoFar(AuthenticationFlowContext context, UserModel user) {
        UserLoginFailureModel failures = context.getSession().loginFailures().getUserLoginFailure(context.getRealm(), user.getId());
        return failures == null ? 0 : failures.getNumFailures();
    }

    private void reject(AuthenticationFlowContext context, String code) {
        context.challenge(errorResponse(Response.Status.BAD_REQUEST.getStatusCode(), "invalid_grant", code));
    }

    @Override
    public boolean requiresUser() {
        return true;
    }

    @Override
    public boolean configuredFor(KeycloakSession session, RealmModel realm, UserModel user) {
        return true;
    }

    @Override
    public void setRequiredActions(KeycloakSession session, RealmModel realm, UserModel user) {
    }

    @Override
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public String getDisplayType() {
        return "Lantern: PIN check";
    }

    /**
     * Keycloak's brute-force protector only counts failures in the password, otp and recovery-code
     * categories; reporting "password" makes wrong PINs count, and a right PIN reset the count.
     */
    @Override
    public String getReferenceCategory() {
        return PasswordCredentialModel.TYPE;
    }

    @Override
    public boolean isConfigurable() {
        return false;
    }

    @Override
    public AuthenticationExecutionModel.Requirement[] getRequirementChoices() {
        return REQUIREMENT_CHOICES;
    }

    @Override
    public String getHelpText() {
        return "Validates 'pin' against the cashier's lantern-pin credential; 'new_pin' replaces a temporary PIN.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return new LinkedList<>();
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }
}
