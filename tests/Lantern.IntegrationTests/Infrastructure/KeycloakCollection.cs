namespace Lantern.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class KeycloakCollection : ICollectionFixture<KeycloakFixture>
{
    public const string Name = "keycloak";
}
