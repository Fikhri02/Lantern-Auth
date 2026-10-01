package dev.lantern.keycloak;

import jakarta.ws.rs.core.Response;
import java.util.LinkedList;
import java.util.List;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.directgrant.AbstractDirectGrantAuthenticator;
import org.keycloak.events.Errors;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Step 2 of till-cashier-pin: the code names an enabled cashier of the till's outlet (spec §6.2).
 * Fails before setting the user, so these failures never count toward anyone's lockout.
 */
public class CashierCheckAuthenticator extends AbstractDirectGrantAuthenticator {

    public static final String PROVIDER_ID = "lantern-cashier-check";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        RealmModel realm = context.getRealm();
        String username = context.getHttpRequest().getDecodedFormParameters().getFirst("username");
        UserModel cashier = username == null || username.isBlank()
                ? null
                : context.getSession().users().getUserByUsername(realm, username.trim());

        RoleModel cashierRole = realm.getRole(CashierRoles.CASHIER);
        if (cashier == null || cashierRole == null || !cashier.hasRole(cashierRole)) {
            fail(context, Errors.USER_NOT_FOUND, TillErrors.CASHIER_NOT_FOUND);
            return;
        }
        if (!cashier.isEnabled()) {
            fail(context, Errors.USER_DISABLED, TillErrors.CASHIER_DISABLED);
            return;
        }

        String tillOutlet = context.getAuthenticationSession().getAuthNote(OutletSessionCheckAuthenticator.NOTE_OUTLET_ID);
        String cashierOutlet = OutletIdMapper.resolveOutletId(cashier.getGroupsStream()).orElse(null);
        if (tillOutlet == null || !tillOutlet.equals(cashierOutlet)) {
            fail(context, Errors.ACCESS_DENIED, TillErrors.CASHIER_WRONG_OUTLET);
            return;
        }

        context.getAuthenticationSession().setUserSessionNote("till_account",
                context.getAuthenticationSession().getAuthNote(OutletSessionCheckAuthenticator.NOTE_TILL_ACCOUNT));
        context.setUser(cashier);
        context.success();
    }

    private void fail(AuthenticationFlowContext context, String event, String code) {
        context.getEvent().error(event);
        context.failure(AuthenticationFlowError.INVALID_USER,
                errorResponse(Response.Status.BAD_REQUEST.getStatusCode(), "invalid_grant", code));
    }

    @Override
    public boolean requiresUser() {
        return false;
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
        return "Lantern: cashier check";
    }

    @Override
    public String getReferenceCategory() {
        return null;
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
        return "Resolves 'username' to an enabled cashier in the same outlet as the till.";
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
