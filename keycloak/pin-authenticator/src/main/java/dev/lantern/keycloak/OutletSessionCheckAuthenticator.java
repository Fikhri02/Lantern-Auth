package dev.lantern.keycloak;

import jakarta.ws.rs.core.Response;
import java.util.LinkedList;
import java.util.List;
import java.util.Optional;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.directgrant.AbstractDirectGrantAuthenticator;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.UserSessionModel;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.representations.AccessToken;
import org.keycloak.services.Urls;
import org.keycloak.util.TokenUtil;

/** Step 1 of till-cashier-pin: the request must carry a live outlet login for this client (spec §6.2). */
public class OutletSessionCheckAuthenticator extends AbstractDirectGrantAuthenticator {

    public static final String PROVIDER_ID = "lantern-outlet-session-check";
    static final String NOTE_OUTLET_ID = "lantern.outlet_id";
    static final String NOTE_TILL_ACCOUNT = "lantern.till_account";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        KeycloakSession session = context.getSession();
        RealmModel realm = context.getRealm();
        String raw = context.getHttpRequest().getDecodedFormParameters().getFirst("outlet_token");

        // decode() verifies the signature only; expiry, issuer and type are ours to check.
        AccessToken token = raw == null ? null : session.tokens().decode(raw, AccessToken.class);
        String issuer = Urls.realmIssuer(context.getUriInfo().getBaseUri(), realm.getName());
        if (token == null || !token.isActive() || !issuer.equals(token.getIssuer())
                || !TokenUtil.TOKEN_TYPE_BEARER.equals(token.getType()) || token.getSessionId() == null) {
            fail(context);
            return;
        }

        UserModel device = session.users().getUserById(realm, token.getSubject());
        RoleModel deviceRole = realm.getRole(CashierRoles.OUTLET_DEVICE);
        if (device == null || !device.isEnabled() || deviceRole == null || !device.hasRole(deviceRole)) {
            fail(context);
            return;
        }

        ClientModel till = context.getAuthenticationSession().getClient();
        UserSessionModel outletLogin = session.sessions().getOfflineUserSession(realm, token.getSessionId());
        if (outletLogin == null || !outletLogin.getUser().getId().equals(device.getId())
                || outletLogin.getAuthenticatedClientSessionByClient(till.getId()) == null) {
            fail(context);
            return;
        }

        Optional<String> outletId = OutletIdMapper.resolveOutletId(device.getGroupsStream());
        if (outletId.isEmpty()) {
            fail(context);
            return;
        }

        context.getAuthenticationSession().setAuthNote(NOTE_OUTLET_ID, outletId.get());
        context.getAuthenticationSession().setAuthNote(NOTE_TILL_ACCOUNT, device.getUsername());
        context.success();
    }

    private void fail(AuthenticationFlowContext context) {
        context.getEvent().error(TillErrors.OUTLET_SESSION_INVALID);
        context.failure(AuthenticationFlowError.INVALID_CLIENT_SESSION,
                errorResponse(Response.Status.BAD_REQUEST.getStatusCode(), "invalid_grant", TillErrors.OUTLET_SESSION_INVALID));
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
        return "Lantern: outlet session check";
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
        return "Requires an 'outlet_token' parameter: a live access token of an outlet-device account signed in to this client.";
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
