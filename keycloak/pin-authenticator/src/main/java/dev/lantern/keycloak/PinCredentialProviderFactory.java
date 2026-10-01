package dev.lantern.keycloak;

import org.keycloak.credential.CredentialProviderFactory;
import org.keycloak.models.KeycloakSession;

public class PinCredentialProviderFactory implements CredentialProviderFactory<PinCredentialProvider> {

    public static final String ID = "lantern-pin";

    @Override
    public PinCredentialProvider create(KeycloakSession session) {
        return new PinCredentialProvider(session);
    }

    @Override
    public String getId() {
        return ID;
    }
}
