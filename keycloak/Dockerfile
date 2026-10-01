FROM maven:3.9-eclipse-temurin-21 AS plugin
WORKDIR /build
COPY pin-authenticator/pom.xml .
RUN mvn -q -B dependency:go-offline
COPY pin-authenticator/src ./src
RUN mvn -q -B package

FROM quay.io/keycloak/keycloak:26.7.5
COPY --from=plugin /build/target/lantern-keycloak-extensions.jar /opt/keycloak/providers/
