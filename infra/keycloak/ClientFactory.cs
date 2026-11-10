using Pulumi;
using Pulumi.Keycloak.OpenId;
using KeycloakClient = Pulumi.Keycloak.OpenId.Client;

namespace Keycloak.Infra;

// Factory of clients: every flow has one method, the common settings (default scopes, audience mappers)
// are extension methods, so a new client is one line and all clients stay consistent.
public sealed class ClientFactory(Output<string> realmId, CustomResourceOptions options)
{
    // A resource server: a confidential client with Authorization Services; its scopes are the permissions.
    // AFFIRMATIVE: a scope is granted if at least ONE permission (role) allows it. With UNANIMOUS a user
    // would also have to pass the permissions of all other roles that cover the same scope.
    public KeycloakClient ResourceServer(string clientId) => new(clientId, new ClientArgs
    {
        RealmId = realmId,
        ClientId = clientId,
        Name = $"Resource server {clientId}",
        AccessType = "CONFIDENTIAL",
        ServiceAccountsEnabled = true,                 // required by Keycloak for Authorization Services
        StandardFlowEnabled = false,
        Authorization = new Pulumi.Keycloak.OpenId.Inputs.ClientAuthorizationArgs
        {
            PolicyEnforcementMode = "ENFORCING",
            DecisionStrategy = "AFFIRMATIVE",
            KeepDefaults = false                         // no "Default Resource" and "Default Policy"
        }
    }, options);

    // Authorization Code for people (Postman): confidential, redirect to the callback of Postman.
    // Direct access grants are on only for the scripted check of the lab on the local stand.
    public KeycloakClient AuthorizationCode(string clientId, params string[] redirectUris) => new(clientId, new ClientArgs
    {
        RealmId = realmId,
        ClientId = clientId,
        Name = $"{clientId} (authorization code)",
        AccessType = "CONFIDENTIAL",
        StandardFlowEnabled = true,
        DirectAccessGrantsEnabled = true,
        PkceCodeChallengeMethod = "S256",
        ValidRedirectUris = redirectUris
    }, options);

    // Public client in a browser (Swagger UI): no secret, so PKCE is required
    public KeycloakClient PublicPkce(string clientId, string[] redirectUris, string[] webOrigins) => new(clientId, new ClientArgs
    {
        RealmId = realmId,
        ClientId = clientId,
        Name = $"{clientId} (public, PKCE)",
        AccessType = "PUBLIC",
        StandardFlowEnabled = true,
        PkceCodeChallengeMethod = "S256",
        ValidRedirectUris = redirectUris,
        WebOrigins = webOrigins
    }, options);

    // Client Credentials for a service (the Aggregator): only a service account, no user flows
    public KeycloakClient ClientCredentials(string clientId) => new(clientId, new ClientArgs
    {
        RealmId = realmId,
        ClientId = clientId,
        Name = $"{clientId} (client credentials)",
        AccessType = "CONFIDENTIAL",
        ServiceAccountsEnabled = true,
        StandardFlowEnabled = false
    }, options);
}

public static class ClientExtensions
{
    // The resource servers must appear in "aud" of the access token, otherwise the services reject it
    public static KeycloakClient WithAudiences(this KeycloakClient client, Output<string> realmId, string name,
        CustomResourceOptions options, params string[] audiences)
    {
        foreach (var audience in audiences)
            _ = new AudienceProtocolMapper($"{name}-aud-{audience}", new AudienceProtocolMapperArgs
            {
                RealmId = realmId,
                ClientId = client.Id,
                Name = $"audience {audience}",
                IncludedClientAudience = audience,
                AddToAccessToken = true,
                AddToIdToken = false
            }, options);
        return client;
    }
}
