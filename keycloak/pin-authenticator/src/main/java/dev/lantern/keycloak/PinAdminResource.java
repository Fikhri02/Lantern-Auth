package dev.lantern.keycloak;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.PUT;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.PathParam;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;
import java.util.Map;
import org.keycloak.models.AdminRoles;
import org.keycloak.models.Constants;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;
import org.keycloak.representations.AccessToken;
import org.keycloak.services.managers.AppAuthManager;
import org.keycloak.services.managers.AuthenticationManager;

/** PUT /realms/{realm}/lantern-pin/users/{id}: set a cashier's PIN. Caller needs realm-management/manage-users. */
public class PinAdminResource {

    public static final class PinRequest {
        public String pin;
        public Boolean temporary;
    }

    private final KeycloakSession session;

    public PinAdminResource(KeycloakSession session) {
        this.session = session;
    }

    @PUT
    @Path("users/{id}")
    @Consumes(MediaType.APPLICATION_JSON)
    public Response setPin(@PathParam("id") String id, PinRequest request) {
        AuthenticationManager.AuthResult auth = new AppAuthManager.BearerTokenAuthenticator(session).authenticate();
        if (auth == null) return Response.status(Response.Status.UNAUTHORIZED).build();
        AccessToken.Access access = auth.token().getResourceAccess(Constants.REALM_MANAGEMENT_CLIENT_ID);
        if (access == null || !access.isUserInRole(AdminRoles.MANAGE_USERS)) return Response.status(Response.Status.FORBIDDEN).build();

        RealmModel realm = session.getContext().getRealm();
        UserModel user = session.users().getUserById(realm, id);
        if (user == null) return error(Response.Status.NOT_FOUND, "user_not_found");
        RoleModel cashier = realm.getRole(CashierRoles.CASHIER);
        if (cashier == null || !user.hasRole(cashier)) return error(Response.Status.BAD_REQUEST, "not_a_cashier");
        if (request == null || !PinRules.isWellFormed(request.pin)) return error(Response.Status.BAD_REQUEST, "pin_rule_format");

        PinCredentials.set(session, user, request.pin, request.temporary == null || request.temporary);
        PinLockout.clear(session, user);
        return Response.noContent().build();
    }

    private static Response error(Response.Status status, String code) {
        return Response.status(status).entity(Map.of("error", code)).type(MediaType.APPLICATION_JSON_TYPE).build();
    }
}
