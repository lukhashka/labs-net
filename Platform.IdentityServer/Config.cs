using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace Platform.IdentityServer;

// Configuration of IdentityServer. It is written once into the configuration store (ConfigurationDbContext)
// on the first start; afterwards the tables Clients, ApiResources, ApiScopes, ... are the source.
public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email()
    ];

    // Fine-grained scopes: a separate permission for reading and writing, not "all or nothing"
    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new("orders.read", "Read orders"),
        new("orders.write", "Create and change orders"),
        new("products.read", "Read the catalog"),
        new("products.write", "Change the catalog"),
        new("reviews.read", "Read reviews")
    ];

    // One ApiResource per microservice: its name becomes the "aud" of the token, the service checks it (Audience)
    public static IEnumerable<ApiResource> ApiResources =>
    [
        new("orders_api", "Orders service") { Scopes = { "orders.read", "orders.write" } },
        new("products_api", "Catalog service") { Scopes = { "products.read", "products.write" } },
        new("reviews_api", "Reviews service") { Scopes = { "reviews.read" } },
        new("payments_api", "Payments service")        // no scope is given to any client: payments are admin-only (Keycloak)
    ];

    public static IEnumerable<Client> Clients(string aggregatorSecret) =>
    [
        // Postman: Authorization Code for a user in a browser; a confidential client with a secret
        new Client
        {
            ClientId = "postman",
            ClientName = "Postman",
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            ClientSecrets = { new Secret("secret".Sha256()) },         // development secret from the lab
            RedirectUris = { "https://oauth.pstmn.io/v1/callback" },
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId, IdentityServerConstants.StandardScopes.Profile,
                "orders.read", "orders.write", "products.read", "products.write"
            },
            AllowOfflineAccess = true,                                 // refresh token
            RequireConsent = false
        },

        // Aggregator: Client Credentials, service to service; the token has no user ("sub") at all
        new Client
        {
            ClientId = "aggregator_service",
            ClientName = "Platform Aggregator",
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            ClientSecrets = { new Secret(aggregatorSecret.Sha256()) },   // only the hash is stored
            AllowedScopes = { "orders.read", "products.read", "reviews.read" }
        },

        // Swagger UI: a public client in the browser, it cannot keep a secret, so PKCE is required
        new Client
        {
            ClientId = "swagger",
            ClientName = "Swagger UI",
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireClientSecret = false,
            RedirectUris =
            {
                "http://localhost:5103/swagger/oauth2-redirect.html", "https://localhost:7103/swagger/oauth2-redirect.html"
            },
            AllowedCorsOrigins = { "http://localhost:5103", "https://localhost:7103" },
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId, IdentityServerConstants.StandardScopes.Profile,
                "orders.read", "orders.write"
            },
            RequireConsent = false
        }
    ];
}
