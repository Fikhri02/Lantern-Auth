package dev.lantern.keycloak;

import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;
import java.util.LinkedList;
import java.util.List;
import java.util.Optional;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.directgrant.AbstractDirectGrantAuthenticator;
import org.keycloak.events.Errors;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Step 3 of till-cashier-pin: checks the PIN, respects brute-force lockout, and replaces a
 * temporary PIN in the same request (spec §6.2–6.4).
 *
 * Wrong PINs are counted by {@link PinLockout}, not by Keycloak's password lockout, so guessing a
 * cashier's password elsewhere can't lock their PIN. Errors other than a wrong PIN use challenge().
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
        if (PinLockout.isLocked(context.getSession(), context.getRealm(), cashier)) {
            context.getEvent().user(cashier).error(Errors.USER_TEMPORARILY_DISABLED);
            reject(context, TillErrors.PIN_LOCKED);
            return;
        }

        switch (PinCredentials.check(context.getSession(), cashier, pin)) {
            case NO_PIN, MISMATCH -> {
                int failuresSoFar = PinLockout.recordFailure(context.getSession(), context.getRealm(), cashier);
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
                PinLockout.clear(context.getSession(), cashier);
                context.success();
            }
            case OK -> {
                PinLockout.clear(context.getSession(), cashier);
                context.success();
            }
        }
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

    /** Not a category Keycloak's brute-force protector counts: PIN failures are PinLockout's job. */
    @Override
    public String getReferenceCategory() {
        return PinCredentials.TYPE;
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
