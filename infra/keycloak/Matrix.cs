namespace Keycloak.Infra;

// The technical task of Part 3: which permission (scope) every role gets on every resource server.
// Tables 6 and 7 of the lab; the endpoints check the same names with [RequirePermission("...")].
public static class Matrix
{
    public static readonly string[] Roles = ["admin", "manager", "user", "guest"];

    public static readonly ResourceServer[] ResourceServers =
    [
        new("orders-api", "orders", "/api/orders/*",
            Scopes: ["orders:read", "orders:create", "orders:update", "orders:delete", "orders:approve"],
            Grants: new()
            {
                ["admin"] = ["orders:read", "orders:create", "orders:update", "orders:delete", "orders:approve"],
                ["manager"] = ["orders:read", "orders:update", "orders:approve"],
                ["user"] = ["orders:read", "orders:create"]
                // guest: nothing
            }),

        new("products-api", "products", "/api/events/*",
            Scopes: ["products:read", "products:create", "products:update", "products:delete", "products:publish"],
            Grants: new()
            {
                ["admin"] = ["products:read", "products:create", "products:update", "products:delete", "products:publish"],
                ["manager"] = ["products:read", "products:update", "products:publish"],
                ["user"] = ["products:read"],
                ["guest"] = ["products:read"]
            }),

        // Payments are the most sensitive resource: on purpose only admin gets permissions
        new("payments-api", "payments", "/api/payments/*",
            Scopes: ["payments:read", "payments:process", "payments:refund"],
            Grants: new()
            {
                ["admin"] = ["payments:read", "payments:process", "payments:refund"]
            })
    ];
}

public sealed record ResourceServer(string ClientId, string ResourceName, string Uri, string[] Scopes,
    Dictionary<string, string[]> Grants);

// One test user per role (Part 3.4)
public sealed record TestUser(string UserName, string FirstName, string LastName, string Role);
