using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Platform.ServiceDefaults.Authentication;

// [RequirePermission("orders:delete")] on an endpoint: the service itself decides, by the permissions in the token.
// Keycloak only says WHAT the user may do; every endpoint checks locally WHETHER this action is allowed.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute(PolicyPrefix + permission)
{
    public const string PolicyPrefix = "permission:";
    public string Permission { get; } = permission;
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

// Creates the policy "permission:orders:read" on the first use: no need to register a policy per permission
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder(PlatformAuthentication.Scheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[RequirePermissionAttribute.PolicyPrefix.Length..]))
            .Build();
    }
}

// An authenticated user without the permission gets 403, an anonymous one 401: never 500
public sealed class PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (PermissionResolver.GetPermissions(context.User).Contains(requirement.Permission))
            context.Succeed(requirement);
        else
            logger.LogInformation("Permission {Permission} denied for {User}", requirement.Permission,
                context.User.FindFirst("preferred_username")?.Value ?? context.User.Identity?.Name ?? context.User.FindFirst("client_id")?.Value);
        return Task.CompletedTask;
    }
}

// Where the permissions of a user come from, depending on the issuer of the token
public static class PermissionResolver
{
    // Auth Service (Part 1) issues roles only; the roles of the Keycloak matrix apply to them
    private static readonly Dictionary<string, string[]> AuthServiceRoles = new()
    {
        ["Admin"] = [.. Matrix.All],
        ["User"] = ["orders:read", "orders:create", "products:read"]
    };

    // IdentityServer (Part 2) issues coarse scopes; a scope covers a group of permissions
    private static readonly Dictionary<string, string[]> IdentityServerScopes = new()
    {
        ["orders.read"] = ["orders:read"],
        ["orders.write"] = ["orders:create", "orders:update", "orders:delete", "orders:approve"],
        ["products.read"] = ["products:read"],
        ["products.write"] = ["products:create", "products:update", "products:delete", "products:publish"]
    };

    public static IReadOnlySet<string> GetPermissions(ClaimsPrincipal user)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        // Keycloak (Part 3): {"permissions":[{"rsname":"orders-api","scopes":["orders:read",...]}]}
        if (user.FindFirst("authorization")?.Value is { } authorization)
        {
            using var doc = JsonDocument.Parse(authorization);
            if (doc.RootElement.TryGetProperty("permissions", out var permissions))
                foreach (var p in permissions.EnumerateArray())
                    if (p.TryGetProperty("scopes", out var scopes))
                        foreach (var s in scopes.EnumerateArray())
                            if (s.GetString() is { } scope)
                                result.Add(scope);
        }

        foreach (var scope in Scopes(user))
            if (IdentityServerScopes.TryGetValue(scope, out var granted))
                result.UnionWith(granted);

        foreach (var role in user.FindAll(PlatformAuthentication.RoleClaim))
            if (AuthServiceRoles.TryGetValue(role.Value, out var granted))
                result.UnionWith(granted);

        return result;
    }

    // "scope" comes as one claim per value (JSON array) or as one space-separated string
    public static IEnumerable<string> Scopes(ClaimsPrincipal user)
        => user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

// The full set of permissions from Table 6 of the lab
public static class Matrix
{
    public static readonly string[] Orders = ["orders:read", "orders:create", "orders:update", "orders:delete", "orders:approve"];
    public static readonly string[] Products = ["products:read", "products:create", "products:update", "products:delete", "products:publish"];
    public static readonly string[] Payments = ["payments:read", "payments:process", "payments:refund"];
    public static IEnumerable<string> All => Orders.Concat(Products).Concat(Payments);
}

public static class PlatformAuthorization
{
    // Scope policies for service-to-service calls with a Client Credentials token (the Aggregator over gRPC)
    public static readonly string[] ScopePolicies = ["orders.read", "products.read", "reviews.read"];

    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(o =>
        {
            foreach (var scope in ScopePolicies)
                o.AddPolicy(scope, p => p
                    .AddAuthenticationSchemes(PlatformAuthentication.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(ctx => PermissionResolver.Scopes(ctx.User).Contains(scope)));
        });
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}
