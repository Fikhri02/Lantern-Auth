package dev.lantern.keycloak;

import jakarta.ws.rs.core.Response;
import java.util.List;
import java.util.Optional;
import org.keycloak.Config;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.AuthenticatorConfigModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Turns away a correctly signed-in user who lacks the app's role (spec §4.5), like Keycloak's
 * deny-access-authenticator, but with forceChallenge instead of a flow failure: the built-in one counts
 * every refusal toward brute-force lockout, so opening the wrong app five times locked people out of
 * the apps they are allowed into.
 */
public class DenyAccessAuthenticator implements Authenticator, AuthenticatorFactory {

    public static final String PROVIDER_ID = "lantern-deny-access";
    static final String MESSAGE_KEY = "denyErrorMessage";
    static final String DEFAULT_MESSAGE = "Your account doesn't have access to this app.";

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        String message = Optional.ofNullable(context.getAuthenticatorConfig())
                .map(AuthenticatorConfigModel::getConfig)
                .map(c -> c.get(MESSAGE_KEY))
                .filter(m -> !m.isBlank())
                .orElse(DEFAULT_MESSAGE);
        context.getEvent().user(context.getUser()).error("access_denied");
        context.forceChallenge(context.form().setError(message).createErrorPage(Response.Status.FORBIDDEN));
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
        return "Lantern: deny access (no lockout)";
    }

    @Override
    public String getReferenceCategory() {
        return null;
    }

    @Override
    public boolean isConfigurable() {
        return true;
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
        return "Shows an error page and stops the flow without counting toward brute-force lockout.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        ProviderConfigProperty message = new ProviderConfigProperty(
                MESSAGE_KEY, "Error message", "Shown to the user who is turned away.", ProviderConfigProperty.STRING_TYPE, null);
        return List.of(message);
    }
}
