package dev.lantern.keycloak;

import jakarta.ws.rs.core.Response;
import java.util.List;
import org.keycloak.Config;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Refuses an outlet sign-in when the account already has a live offline till login (spec §6.1 E).
 * Keycloak's built-in session limiter only counts online sessions, and the till ends its online one.
 * Uses forceChallenge so refusals don't count toward the account's brute-force lockout.
 */
public class TillAccountLimitAuthenticator implements Authenticator, AuthenticatorFactory {

    public static final String PROVIDER_ID = "lantern-till-account-limit";
    static final String IN_USE_MESSAGE =
            "This till account is already signed in on another till. Ask your outlet manager to release it.";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        UserModel account = context.getUser();
        ClientModel till = context.getAuthenticationSession().getClient();
        boolean inUse = context.getSession().sessions().getOfflineUserSessionsStream(context.getRealm(), account)
                .anyMatch(s -> s.getAuthenticatedClientSessionByClient(till.getId()) != null);
        if (inUse) {
            context.getEvent().user(account).error("till_account_in_use");
            context.forceChallenge(context.form().setError(IN_USE_MESSAGE).createErrorPage(Response.Status.FORBIDDEN));
            return;
        }
        context.success();
    }

    @Override
    public void action(AuthenticationFlowContext context) {
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
    public Authenticator create(KeycloakSession session) {
        return this;
    }

    @Override
    public void init(Config.Scope config) {
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Lantern: one till per account";
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
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public String getHelpText() {
        return "Refuses sign-in when the account already has a live offline login for this client.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return List.of();
    }
}
