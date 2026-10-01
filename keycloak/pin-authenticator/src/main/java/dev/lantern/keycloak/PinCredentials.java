package dev.lantern.keycloak;

import java.io.IOException;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.keycloak.common.util.MultivaluedHashMap;
import org.keycloak.common.util.Time;
import org.keycloak.credential.CredentialModel;
import org.keycloak.credential.hash.PasswordHashProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.UserModel;
import org.keycloak.models.credential.PasswordCredentialModel;
import org.keycloak.models.credential.dto.PasswordCredentialData;
import org.keycloak.util.JsonSerialization;

/**
 * Stores a cashier PIN as a hashed credential of type {@value #TYPE}, using the realm's password
 * hashing (spec §6.6). The temporary flag rides in the hash parameters ("algorithmData").
 */
public final class PinCredentials {

    public static final String TYPE = "lantern-pin";
    static final String TEMPORARY_FLAG = "lantern-temporary";

    public enum Check { NO_PIN, MISMATCH, OK, OK_TEMPORARY }

    private PinCredentials() {
    }

    public static void set(KeycloakSession session, UserModel user, String pin, boolean temporary) {
        PasswordHashProvider hasher = session.getProvider(PasswordHashProvider.class);
        PasswordCredentialModel hashed = hasher.encodedCredential(pin, -1);
        PasswordCredentialData data = hashed.getPasswordCredentialData();

        Map<String, List<String>> params = new HashMap<>();
        if (data.getAdditionalParameters() != null) params.putAll(data.getAdditionalParameters());
        if (temporary) params.put(TEMPORARY_FLAG, List.of("true"));

        CredentialModel credential = new CredentialModel();
        credential.setType(TYPE);
        credential.setUserLabel(temporary ? "PIN (temporary)" : "PIN");
        credential.setCreatedDate(Time.currentTimeMillis());
        credential.setSecretData(hashed.getSecretData());
        try {
            credential.setCredentialData(JsonSerialization.writeValueAsString(
                    new PasswordCredentialData(data.getHashIterations(), data.getAlgorithm(), params)));
        } catch (IOException e) {
            throw new IllegalStateException("Could not serialise the PIN credential", e);
        }

        user.credentialManager().getStoredCredentialsByTypeStream(TYPE).toList()
                .forEach(old -> user.credentialManager().removeStoredCredentialById(old.getId()));
        user.credentialManager().createStoredCredential(credential);
    }

    public static Check check(KeycloakSession session, UserModel user, String pin) {
        CredentialModel stored = user.credentialManager().getStoredCredentialsByTypeStream(TYPE).findFirst().orElse(null);
        if (stored == null) return Check.NO_PIN;

        PasswordCredentialModel model = PasswordCredentialModel.createFromCredentialModel(stored);
        PasswordHashProvider hasher = session.getProvider(PasswordHashProvider.class,
                model.getPasswordCredentialData().getAlgorithm());
        if (hasher == null || pin == null || !hasher.verify(pin, model)) return Check.MISMATCH;

        MultivaluedHashMap<String, String> params = model.getPasswordCredentialData().getAdditionalParameters();
        boolean temporary = params != null && "true".equals(params.getFirst(TEMPORARY_FLAG));
        return temporary ? Check.OK_TEMPORARY : Check.OK;
    }
}
