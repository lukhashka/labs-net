// Configuration only: no business logic here. Runs all resources during development.
var builder = DistributedApplication.CreateBuilder(args);

// The password is a secret parameter: dotnet user-secrets set "Parameters:sql-password" "<password>"
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword).WithDataVolume();
var ordersDb = sql.AddDatabase("OrdersDb");                 // Lab 02
var catalogDb = sql.AddDatabase("CatalogDb");               // Lab 03

// Lab 09: the databases of the identity providers are separate from the business databases.
// They share the SQL Server container to save memory of the development PC (Docker has 8 GB);
// in production IdentityServerDb gets its own server: builder.AddSqlServer("identityserver-sql").
var identityDb = sql.AddDatabase("IdentityDb");             // Auth Service (ASP.NET Identity + JWT)
var identityServerDb = sql.AddDatabase("IdentityServerDb"); // Duende IdentityServer: users, configuration, grants

// Lab 10: the passwords of the containers are explicit secret parameters. With the names Aspire used for its
// generated passwords, the values already kept in the user secrets (and the existing volumes) stay valid,
// and aspirate puts them into Kubernetes Secrets instead of plain ConfigMaps.
var mongoPassword = builder.AddParameter("mongo-password", secret: true);
var redisPassword = builder.AddParameter("redis-password", secret: true);
var messagingPassword = builder.AddParameter("messaging-password", secret: true);
var keycloakPassword = builder.AddParameter("keycloak-password", secret: true);

var mongo = builder.AddMongoDB("mongo", password: mongoPassword).WithDataVolume();
var reviewsDb = mongo.AddDatabase("ReviewsDb");             // Lab 04

var redis = builder.AddRedis("redis", password: redisPassword).WithDataVolume();

// Lab 08: the message broker. The volume keeps queued messages over restarts, the management UI shows
// exchanges, queues and DLQs. Aspire generates the user and the password.
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithDataVolume()
    .WithManagementPlugin();

// Lab 09: the secret that signs the tokens of the Auth Service. Aspire generates it once and keeps it
// in the user secrets of the AppHost; it never appears in the repository.
var jwtSecret = builder.AddParameter("jwt-secret",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);

// Lab 09, Part 3 (trajectory B): Keycloak through the Aspire integration; the volume keeps the realm.
// The admin password is the secret parameter keycloak-password (user secrets of the AppHost).
// The realm DevRealmDrivers, clients, roles and users are created by Pulumi: infra/keycloak.
var keycloak = builder.AddKeycloak("keycloak", 8080, adminPassword: keycloakPassword)
    .WithDataVolume();

// Lab 09, Part 1: own authentication service
var auth = builder.AddProject<Projects.Auth_Api>("auth-api")
    .WithPlatformReferences()
    .WithReference(identityDb).WaitFor(identityDb)
    .WithHttpHealthCheck("/health");
if (builder.ExecutionContext.IsRunMode)
    auth.WithEnvironment("Jwt__SecretKey", jwtSecret);    // in Kubernetes: Secret platform-jwt

// Lab 09, Part 2: Duende IdentityServer on a fixed port (5000 is the gateway): http://localhost:5010
var identityServer = builder.AddProject<Projects.Platform_IdentityServer>("identityserver")
    .WithPlatformReferences()
    .WithReference(identityServerDb).WaitFor(identityServerDb)
    .WithHttpHealthCheck("/health");

// The resource name becomes the logical name for service discovery.
// WithHttpHealthCheck (Lab 07): the Dashboard shows Healthy / Degraded / Unhealthy of every service,
// and WaitFor of the next resource waits for a healthy dependency, not only for a started process.
// The https profile: gRPC needs HTTP/2, on Kestrel it is negotiated over TLS (ALPN). REST stays on the same service.
// Lab 09: every service gets the secret of the Auth Service and the addresses of IdentityServer and Keycloak,
// so it validates the tokens of all three providers locally.
var orders = builder.AddProject<Projects.Orders_Api>("orders-api", launchProfileName: "https")
    .WithPlatformReferences()
    .WithReference(ordersDb).WaitFor(ordersDb)
    .WithReference(messaging).WaitFor(messaging)              // consumes Catalog events
    .WithTokenValidation(jwtSecret, identityServer, keycloak)
    .WithHttpHealthCheck("/health", endpointName: "http");

var catalog = builder.AddProject<Projects.Catalog_Api>("catalog-api", launchProfileName: "https")
    .WithPlatformReferences()
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithReference(messaging).WaitFor(messaging)              // publishes events from its outbox
    .WithReference(redis).WaitFor(redis)
    .WithEnvironment("Caching__Enabled", cacheEnabled())
    .WithTokenValidation(jwtSecret, identityServer, keycloak)
    .WithHttpHealthCheck("/health", endpointName: "http");

var reviews = builder.AddProject<Projects.Reviews_WebApi>("reviews-api", launchProfileName: "https")
    .WithPlatformReferences()
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(messaging).WaitFor(messaging)              // consumes Catalog events
    .WithTokenValidation(jwtSecret, identityServer, keycloak)
    .WithHttpHealthCheck("/health", endpointName: "http");

// Lab 09: demo service of the permission matrix (payments are admin-only)
var payments = builder.AddProject<Projects.Payments_Api>("payments-api")
    .WithPlatformReferences()
    .WithTokenValidation(jwtSecret, identityServer, keycloak)
    .WithHttpHealthCheck("/health");

// The Aggregator gets a Client Credentials token from IdentityServer before it calls the services
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
    .WithPlatformReferences()
    .WithReference(orders).WithReference(catalog).WithReference(reviews)
    .WaitFor(orders).WaitFor(catalog).WaitFor(reviews)
    .WithReference(redis).WaitFor(redis)
    .WithEnvironment("Caching__Enabled", cacheEnabled())
    .WithTokenValidation(jwtSecret, identityServer, keycloak)
    .WaitFor(identityServer)
    .WithHttpHealthCheck("/health", endpointName: "http");

builder.AddProject<Projects.Platform_ApiGateway>("gateway")
    .WithPlatformReferences()
    .WithHttpEndpoint(port: 5000, name: "public")
    .WithReference(orders).WithReference(catalog).WithReference(reviews)
    .WithReference(aggregator)
    .WithReference(auth).WithReference(payments)              // Lab 09: /auth, /users, /api/payments
    .WithTokenValidation(jwtSecret, identityServer, keycloak)
    .WaitFor(aggregator)                                      // waits until the Aggregator is Healthy, not only started
    .WithHttpHealthCheck("/health", endpointName: "http");

builder.Build().Run();

// Benchmark:CacheEnabled=false switches caches off for a fair comparison of HTTP and gRPC
string cacheEnabled() => builder.Configuration["Benchmark:CacheEnabled"] ?? "true";

internal static class TokenValidationExtensions
{
    // Keycloak is not awaited: it starts slowly and is needed only when a Keycloak token arrives.
    // Lab 10: the signing key goes into the environment only when the AppHost runs the platform. In publish mode
    // (aspirate) it is left out, so it never lands in the generated manifests; Kubernetes gets it from the Secret platform-jwt
    // created by kubectl; aspirate-generate.ps1 adds it to every Deployment (deploy/k8s/README.md).
    public static IResourceBuilder<ProjectResource> WithTokenValidation(this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<ParameterResource> jwtSecret, IResourceBuilder<ProjectResource> identityServer,
        IResourceBuilder<KeycloakResource> keycloak)
    {
        if (project.ApplicationBuilder.ExecutionContext.IsRunMode)
            project.WithEnvironment("Jwt__SecretKey", jwtSecret);
        return project.WithReference(identityServer).WithReference(keycloak);
    }

    // Lab 10: a reference gives a service only ConnectionStrings__* and services__* (service discovery).
    // Aspire 13 adds the parts of a connection (HOST, PORT, PASSWORD, URI) too; the services do not read them,
    // and aspirate would write the passwords into plain ConfigMaps.
    public static IResourceBuilder<ProjectResource> WithPlatformReferences(this IResourceBuilder<ProjectResource> project)
        => project.WithReferenceEnvironment(
            ReferenceEnvironmentInjectionFlags.ConnectionString | ReferenceEnvironmentInjectionFlags.ServiceDiscovery);
}
