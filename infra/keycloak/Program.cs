using Keycloak.Infra;
using Pulumi;
using Pulumi.Keycloak;
using Pulumi.Keycloak.Inputs;
using Pulumi.Keycloak.OpenId;
using Pulumi.Keycloak.OpenId.Inputs;

// Lab 09, Part 3: everything in the realm is code. "pulumi preview" shows the plan, "pulumi up" creates it,
// "pulumi destroy" removes it. The admin password of Keycloak is a secret of the stack (pulumi config set --secret).
return await Deployment.RunAsync(() =>
{
    var config = new Pulumi.Config();
    var keycloakUrl = config.Get("keycloakUrl") ?? "http://localhost:8080";
    var testPasswordsTemporary = config.GetBoolean("temporaryPasswords") ?? true;   // users change them at the first login

    var provider = new Provider("keycloak", new ProviderArgs
    {
        Url = keycloakUrl,
        ClientId = "admin-cli",
        Username = config.Get("adminUser") ?? "admin",
        Password = config.RequireSecret("adminPassword"),
        Realm = "master"
    });
    var options = new CustomResourceOptions { Provider = provider };

    // --- Realm
    var realm = new Realm("dev-realm", new RealmArgs
    {
        RealmName = "DevRealmDrivers",
        DisplayName = "CampusTix (development)",
        Enabled = true,
        RegistrationAllowed = true,
        ResetPasswordAllowed = true,
        RememberMe = true,
        VerifyEmail = false,
        LoginTheme = "keycloak",
        AccessTokenLifespan = "15m"
    }, options);
    var realmId = realm.Id;

    // --- Realm roles. admin is a composite role (it contains manager and user): the permissions are grouped,
    //     an admin passes the policies of these roles too. The permissions themselves come from the policies below.
    var roles = new Dictionary<string, Role>();
    foreach (var r in Matrix.Roles.Where(r => r != "admin"))
        roles[r] = new Role($"role-{r}", new RoleArgs { RealmId = realmId, Name = r, Description = $"Realm role {r} of CampusTix" }, options);
    roles["admin"] = new Role("role-admin", new RoleArgs
    {
        RealmId = realmId,
        Name = "admin",
        Description = "Realm role admin of CampusTix: composite of manager and user",
        CompositeRoles = { roles["manager"].Id, roles["user"].Id }
    }, options);

    // --- Resource servers, their scopes (permissions), a resource, a policy per role and a permission per role
    var factory = new ClientFactory(realmId, options);
    var resourceServers = new Dictionary<string, Pulumi.Keycloak.OpenId.Client>();
    foreach (var rs in Matrix.ResourceServers)
    {
        var client = factory.ResourceServer(rs.ClientId);
        resourceServers[rs.ClientId] = client;

        var scopes = rs.Scopes.ToDictionary(s => s, s => new ClientAuthorizationScope($"{rs.ClientId}-{s.Replace(':', '-')}",
            new ClientAuthorizationScopeArgs { RealmId = realmId, ResourceServerId = client.ResourceServerId, Name = s }, options));

        var resource = new ClientAuthorizationResource($"{rs.ClientId}-resource", new ClientAuthorizationResourceArgs
        {
            RealmId = realmId,
            ResourceServerId = client.ResourceServerId,
            Name = rs.ResourceName,
            Uris = { rs.Uri },
            Scopes = rs.Scopes
        }, new CustomResourceOptions { Provider = provider, DependsOn = scopes.Values.Cast<Resource>().ToList() });

        foreach (var (role, granted) in rs.Grants)
        {
            var policy = new ClientRolePolicy($"{rs.ClientId}-{role}-policy", new ClientRolePolicyArgs
            {
                RealmId = realmId,
                ResourceServerId = client.ResourceServerId,
                Name = $"role {role}",
                Type = "role",
                Logic = "POSITIVE",
                DecisionStrategy = "UNANIMOUS",
                Roles = { new ClientRolePolicyRoleArgs { Id = roles[role].Id, Required = true } }
            }, options);

            _ = new ClientAuthorizationPermission($"{rs.ClientId}-{role}-permission", new ClientAuthorizationPermissionArgs
            {
                RealmId = realmId,
                ResourceServerId = client.ResourceServerId,
                Name = $"{role} on {rs.ResourceName}",
                Type = "scope",
                Resources = { resource.Id },
                Scopes = granted.Select(s => scopes[s].Id).ToList(),
                Policies = { policy.Id },
                DecisionStrategy = "AFFIRMATIVE"
            }, options);
        }
    }
    var audiences = Matrix.ResourceServers.Select(r => r.ClientId).ToArray();

    // --- Clients of the three flows through the factory; the audience mappers through an extension method
    var postman = factory.AuthorizationCode("orders.postman", "https://oauth.pstmn.io/v1/callback")
        .WithAudiences(realmId, "postman", options, audiences);
    var swagger = factory.PublicPkce("orders.swagger",
            ["http://localhost:5103/swagger/oauth2-redirect.html", "https://localhost:7103/swagger/oauth2-redirect.html"],
            ["http://localhost:5103", "https://localhost:7103"])
        .WithAudiences(realmId, "swagger", options, audiences);
    var aggregator = factory.ClientCredentials("orders.aggregator")
        .WithAudiences(realmId, "aggregator", options, "orders-api", "products-api");

    // Client-level role for the service account of the Aggregator: it may only read
    var serviceReader = new Role("orders-api-service-reader", new RoleArgs
    {
        RealmId = realmId,
        ClientId = resourceServers["orders-api"].Id,
        Name = "service-reader",
        Description = "Read access for service accounts"
    }, options);
    _ = new ClientServiceAccountRole("aggregator-service-reader", new ClientServiceAccountRoleArgs
    {
        RealmId = realmId,
        ServiceAccountUserId = aggregator.ServiceAccountUserId,
        ClientId = resourceServers["orders-api"].Id,
        Role = serviceReader.Name
    }, options);

    // --- Test users, one per role
    var users = new TestUser[]
    {
        new("alice.admin", "Alice", "Admin", "admin"),
        new("john.manager", "John", "Manager", "manager"),
        new("carol.user", "Carol", "User", "user"),
        new("dave.guest", "Dave", "Guest", "guest")
    };
    var testPassword = config.RequireSecret("testUserPassword");
    foreach (var u in users)
    {
        var user = new User(u.UserName, new UserArgs
        {
            RealmId = realmId,
            Username = u.UserName,
            Email = $"{u.UserName}@campustix.local",
            EmailVerified = true,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Enabled = true,
            InitialPassword = new UserInitialPasswordArgs { Value = testPassword, Temporary = testPasswordsTemporary }
        }, options);

        _ = new UserRoles($"{u.UserName}-roles", new UserRolesArgs
        {
            RealmId = realmId,
            UserId = user.Id,
            RoleIds = { roles[u.Role].Id }
        }, options);
    }

    var realmUrl = Output.Format($"{keycloakUrl}/realms/{realm.RealmName}");
    return new Dictionary<string, object?>
    {
        ["RealmUrl"] = realmUrl,
        ["AdminConsoleUrl"] = Output.Format($"{keycloakUrl}/admin/master/console/#/{realm.RealmName}"),
        ["TokenEndpoint"] = Output.Format($"{realmUrl}/protocol/openid-connect/token"),
        ["ClientIds"] = Output.All(postman.ClientId, swagger.ClientId, aggregator.ClientId),
        ["ClientSecrets"] = Output.CreateSecret(Output.Tuple(postman.ClientSecret, aggregator.ClientSecret)
            .Apply(t => new Dictionary<string, string> { ["orders.postman"] = t.Item1, ["orders.aggregator"] = t.Item2 })),
        ["ResourceServers"] = audiences,
        ["TestUsers"] = users.Select(u => $"{u.UserName} ({u.Role})").ToArray()
    };
});
